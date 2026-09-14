namespace Augit.App;

internal sealed class NativeImageDecoder(Func<string, WicBitmap>? decode = null)
{
    private static readonly SemaphoreSlim DecodeGate = new(1, 1);
    internal static NativeImageDecoder Shared { get; } = new();

    internal async Task<WicBitmap> LoadAsync(string path, CancellationToken cancellationToken)
    {
        await DecodeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                WicBitmap bitmap = (decode ?? WicBitmapLoader.Load)(path);
                if (cancellationToken.IsCancellationRequested)
                {
                    bitmap.Dispose();
                    cancellationToken.ThrowIfCancellationRequested();
                }
                return bitmap;
            }, cancellationToken).ConfigureAwait(false);
        }
        finally { DecodeGate.Release(); }
    }
}
