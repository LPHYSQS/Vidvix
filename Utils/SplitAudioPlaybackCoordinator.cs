using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Vidvix.Utils;

public interface ISplitAudioPlaybackParticipant
{
    Task PauseForPlaybackCoordinationAsync();
}

public static class SplitAudioPlaybackCoordinator
{
    private static readonly object SyncRoot = new();
    private static readonly SemaphoreSlim CoordinationSemaphore = new(1, 1);
    private static readonly List<WeakReference<ISplitAudioPlaybackParticipant>> Participants = new();
    private static ISplitAudioPlaybackParticipant? _activeParticipant;

    public static void Register(ISplitAudioPlaybackParticipant participant)
    {
        ArgumentNullException.ThrowIfNull(participant);

        lock (SyncRoot)
        {
            CleanupDeadParticipants();
            foreach (var reference in Participants)
            {
                if (reference.TryGetTarget(out var existingParticipant) &&
                    ReferenceEquals(existingParticipant, participant))
                {
                    return;
                }
            }

            Participants.Add(new WeakReference<ISplitAudioPlaybackParticipant>(participant));
        }
    }

    public static void Unregister(ISplitAudioPlaybackParticipant participant)
    {
        ArgumentNullException.ThrowIfNull(participant);

        lock (SyncRoot)
        {
            for (var index = Participants.Count - 1; index >= 0; index--)
            {
                if (!Participants[index].TryGetTarget(out var existingParticipant) ||
                    ReferenceEquals(existingParticipant, participant))
                {
                    Participants.RemoveAt(index);
                }
            }

            if (ReferenceEquals(_activeParticipant, participant))
            {
                _activeParticipant = null;
            }
        }
    }

    public static async Task RequestPlaybackAsync(ISplitAudioPlaybackParticipant participant)
    {
        ArgumentNullException.ThrowIfNull(participant);

        ISplitAudioPlaybackParticipant? participantToPause = null;

        await CoordinationSemaphore.WaitAsync().ConfigureAwait(false);
        try
        {
            lock (SyncRoot)
            {
                CleanupDeadParticipants();
                if (!ReferenceEquals(_activeParticipant, participant))
                {
                    participantToPause = _activeParticipant;
                    _activeParticipant = participant;
                }
            }
        }
        finally
        {
            CoordinationSemaphore.Release();
        }

        if (participantToPause is not null)
        {
            await participantToPause.PauseForPlaybackCoordinationAsync().ConfigureAwait(false);
        }
    }

    public static async Task PauseAllExceptAsync(ISplitAudioPlaybackParticipant? exemptParticipant = null)
    {
        List<ISplitAudioPlaybackParticipant> participantsToPause = new();

        await CoordinationSemaphore.WaitAsync().ConfigureAwait(false);
        try
        {
            lock (SyncRoot)
            {
                CleanupDeadParticipants();
                foreach (var reference in Participants)
                {
                    if (!reference.TryGetTarget(out var participant) ||
                        ReferenceEquals(participant, exemptParticipant))
                    {
                        continue;
                    }

                    participantsToPause.Add(participant);
                }

                if (_activeParticipant is not null &&
                    !ReferenceEquals(_activeParticipant, exemptParticipant))
                {
                    _activeParticipant = null;
                }
            }
        }
        finally
        {
            CoordinationSemaphore.Release();
        }

        foreach (var participant in participantsToPause)
        {
            await participant.PauseForPlaybackCoordinationAsync().ConfigureAwait(false);
        }
    }

    public static void NotifyPaused(ISplitAudioPlaybackParticipant participant)
    {
        ArgumentNullException.ThrowIfNull(participant);

        lock (SyncRoot)
        {
            CleanupDeadParticipants();
            if (ReferenceEquals(_activeParticipant, participant))
            {
                _activeParticipant = null;
            }
        }
    }

    private static void CleanupDeadParticipants()
    {
        for (var index = Participants.Count - 1; index >= 0; index--)
        {
            if (!Participants[index].TryGetTarget(out _))
            {
                Participants.RemoveAt(index);
            }
        }

        if (_activeParticipant is not null)
        {
            var isActiveParticipantAlive = false;
            foreach (var reference in Participants)
            {
                if (reference.TryGetTarget(out var participant) &&
                    ReferenceEquals(participant, _activeParticipant))
                {
                    isActiveParticipantAlive = true;
                    break;
                }
            }

            if (!isActiveParticipantAlive)
            {
                _activeParticipant = null;
            }
        }
    }
}
