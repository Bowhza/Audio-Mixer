using System;
using System.Threading;

namespace AudioMixer.Services;

/// <summary>
/// Makes sure only one copy of the app runs per user session. Two copies would
/// fight over the mixer's COM port (the second one also resets the board when it
/// probes the port). A second launch signals the running copy to show its window
/// and then exits.
/// </summary>
public static class SingleInstance
{
    private const string MutexName = @"Local\AudioMixer.SingleInstance.7E3C1B52";
    private const string ShowEventName = @"Local\AudioMixer.ShowWindow.7E3C1B52";

    private static Mutex? _mutex;
    private static EventWaitHandle? _showEvent;

    /// <summary>
    /// Claims the single-instance lock. Returns false when another copy already
    /// owns it, after asking that copy to bring its window to the front.
    /// </summary>
    public static bool TryAcquire()
    {
        _mutex = new Mutex(initiallyOwned: true, MutexName, out bool createdNew);
        if (!createdNew)
        {
            // Another instance may have crashed and left the mutex abandoned.
            try
            {
                createdNew = _mutex.WaitOne(0);
            }
            catch (AbandonedMutexException)
            {
                createdNew = true;
            }
        }

        if (!createdNew)
        {
            _mutex.Dispose();
            _mutex = null;
            try
            {
                using var existing = EventWaitHandle.OpenExisting(ShowEventName);
                existing.Set();
            }
            catch
            {
                // The running copy has not created its event yet. Exiting is still correct.
            }
            return false;
        }

        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        return true;
    }

    /// <summary>
    /// Invokes <paramref name="onShowRequested"/> (on a background thread) every
    /// time another launch asks this instance to show itself.
    /// </summary>
    public static void ListenForShowRequests(Action onShowRequested)
    {
        var handle = _showEvent;
        if (handle == null)
            return;

        var thread = new Thread(() =>
        {
            try
            {
                while (handle.WaitOne())
                    onShowRequested();
            }
            catch (ObjectDisposedException)
            {
                // Shutting down.
            }
        })
        {
            IsBackground = true,
            Name = "AudioMixer single-instance listener",
        };
        thread.Start();
    }

    public static void Release()
    {
        try
        {
            _mutex?.ReleaseMutex();
        }
        catch
        {
            // Not owned by this thread or already released.
        }
        _mutex?.Dispose();
        _mutex = null;
        _showEvent?.Dispose();
        _showEvent = null;
    }
}
