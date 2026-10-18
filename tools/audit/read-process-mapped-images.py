"""只读扫描指定实例的映射文件，识别不出现在Windows模块列表中的托管程序集。"""
import argparse
import ctypes
from ctypes import wintypes
import hashlib
import json
from pathlib import Path


def resolve_device_path(name, drives):
    for device, drive in sorted(drives.items(), key=lambda item: -len(item[0])):
        if name.casefold().startswith((device + '\\').casefold()):
            return drive + name[len(device):]
    raise ValueError('无法解析映射文件设备路径：' + name)


def inspect(process_id):
    kernel = ctypes.WinDLL('kernel32', use_last_error=True)
    psapi = ctypes.WinDLL('psapi', use_last_error=True)
    class MemoryInformation(ctypes.Structure):
        _fields_ = [('base', ctypes.c_void_p), ('allocationBase', ctypes.c_void_p),
                    ('allocationProtect', wintypes.DWORD), ('partition', wintypes.WORD),
                    ('regionSize', ctypes.c_size_t), ('state', wintypes.DWORD),
                    ('protect', wintypes.DWORD), ('kind', wintypes.DWORD)]
    kernel.OpenProcess.argtypes = [wintypes.DWORD, wintypes.BOOL, wintypes.DWORD]
    kernel.OpenProcess.restype = wintypes.HANDLE
    kernel.CloseHandle.argtypes = [wintypes.HANDLE]
    kernel.VirtualQueryEx.argtypes = [wintypes.HANDLE, ctypes.c_void_p, ctypes.POINTER(MemoryInformation), ctypes.c_size_t]
    kernel.VirtualQueryEx.restype = ctypes.c_size_t
    kernel.QueryDosDeviceW.argtypes = [wintypes.LPCWSTR, wintypes.LPWSTR, wintypes.DWORD]
    psapi.GetMappedFileNameW.argtypes = [wintypes.HANDLE, ctypes.c_void_p, wintypes.LPWSTR, wintypes.DWORD]
    psapi.GetMappedFileNameW.restype = wintypes.DWORD
    handle = kernel.OpenProcess(0x410, False, process_id)
    if not handle:
        raise ctypes.WinError(ctypes.get_last_error())
    try:
        drives = {}
        for letter in 'ABCDEFGHIJKLMNOPQRSTUVWXYZ':
            buffer = ctypes.create_unicode_buffer(32768)
            if kernel.QueryDosDeviceW(letter + ':', buffer, len(buffer)):
                drives[buffer.value] = letter + ':'
        address, names = 0, set()
        info = MemoryInformation()
        while kernel.VirtualQueryEx(handle, address, ctypes.byref(info), ctypes.sizeof(info)):
            if info.state == 0x1000 and info.kind in (0x20000, 0x1000000):
                buffer = ctypes.create_unicode_buffer(32768)
                if psapi.GetMappedFileNameW(handle, address, buffer, len(buffer)):
                    basename = buffer.value.rsplit('\\', 1)[-1].casefold()
                    if basename in ('augit.dll', 'augit.infrastructure.dll', 'audiodevprops2.dll'):
                        names.add(resolve_device_path(buffer.value, drives))
            following = (info.base or 0) + info.regionSize
            if following <= address:
                raise ValueError('虚拟内存扫描未向前推进')
            address = following
        rows = []
        for name in sorted(names):
            with Path(name).open('rb') as file:
                digest = hashlib.file_digest(file, 'sha256').hexdigest()
            rows.append({'name': Path(name).name, 'path': name, 'sha256': digest})
        if not any(row['name'].casefold() == 'augit.infrastructure.dll' for row in rows):
            raise ValueError('未找到该实例实际映射的Infrastructure程序集')
        return {'pid': process_id, 'mappedImages': rows,
                'limitation': '路径来自目标进程虚拟内存映射；哈希读取对应磁盘文件，不是进程内存字节哈希。'}
    finally:
        kernel.CloseHandle(handle)


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('pid', type=int)
    print(json.dumps(inspect(parser.parse_args().pid), ensure_ascii=False))
