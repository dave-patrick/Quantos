namespace Quantos.Backend.Hosting;

/// <summary>
/// Counts the Windows client's open push channels. Browser pages ask for it so a reminder that the
/// Windows client already shows natively isn't shown a second time in the browser.
/// </summary>
public sealed class ClientPresence
{
    private int _windows;

    public bool WindowsClientConnected => Volatile.Read(ref _windows) > 0;

    public IDisposable WindowsClient()
    {
        Interlocked.Increment(ref _windows);
        return new Leave(this);
    }

    private sealed class Leave(ClientPresence owner) : IDisposable
    {
        private int _done;
        public void Dispose() { if (Interlocked.Exchange(ref _done, 1) == 0) Interlocked.Decrement(ref owner._windows); }
    }
}
