using System.Text.Json;

namespace Augit.Shell;

/// <summary>窗口协议只截获合法的模态信封，其余请求交回既有桥接错误处理。</summary>
internal sealed record ShellModalRequest(long Id, string Method, JsonElement Parameters)
{
    internal static bool TryParse(string json, out ShellModalRequest? request)
    {
        request = null;
        try
        {
            using JsonDocument envelope = JsonDocument.Parse(json);
            JsonElement root = envelope.RootElement;
            using JsonDocument? nested = root.ValueKind == JsonValueKind.String
                ? JsonDocument.Parse(root.GetString()!) : null;
            if (nested is not null) root = nested.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("method", out JsonElement methodElement)
                || methodElement.ValueKind != JsonValueKind.String
                || !root.TryGetProperty("id", out JsonElement idElement)
                || idElement.ValueKind != JsonValueKind.Number || !idElement.TryGetInt64(out long id)) return false;
            string method = methodElement.GetString()!;
            if (method is not ("window/modal-present" or "window/modal-close" or "window/modal-state" or "window/modal-drag" or "window/modal-refresh" or "window/modal-cover" or "window/modal-uncover")) return false;
            JsonElement parameters = root.TryGetProperty("params", out JsonElement value) ? value.Clone() : default;
            request = new(id, method, parameters);
            return true;
        }
        catch (JsonException) { return false; }
    }

    internal int SnapshotDepth()
    {
        if (Parameters.ValueKind != JsonValueKind.Object || !Parameters.TryGetProperty("depth", out JsonElement value)
            || value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out int depth) || depth < 0)
            throw new ArgumentException("背景快照层级无效。");
        return depth;
    }

    internal ShellModalPresentation Presentation(int viewportWidth, int viewportHeight, double scale)
    {
        if (Parameters.ValueKind != JsonValueKind.Object) throw new ArgumentException("缺少模态窗口参数。");
        string Text(string name)
            => Parameters.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
                && value.GetString() is { Length: > 0 } text ? text : throw new ArgumentException("模态窗口文字参数无效。");
        double Number(string name)
            => Parameters.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Number
                && value.TryGetDouble(out double number) ? number : throw new ArgumentException("模态窗口尺寸参数无效。");
        if (!Parameters.TryGetProperty("ancestors", out JsonElement ancestors) || ancestors.ValueKind != JsonValueKind.Array)
            throw new ArgumentException("模态窗口所有者参数无效。");
        HashSet<string> keys = [];
        foreach (JsonElement item in ancestors.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String || item.GetString() is not { Length: > 0 } key)
                throw new ArgumentException("模态窗口所有者标识无效。");
            keys.Add(key);
        }
        return new(Text("key"), Text("title"), keys,
            ShellModalGeometry.Clip(Number("x"), Number("y"), Number("width"), Number("height"), viewportWidth, viewportHeight, scale));
    }
}

internal sealed record ShellModalPresentation(string Key, string Title, IReadOnlySet<string> Ancestors, WindowFrameRect Clip);
