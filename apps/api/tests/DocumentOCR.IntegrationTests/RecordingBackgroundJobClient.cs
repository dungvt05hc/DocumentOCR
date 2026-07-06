using System.Collections.Concurrent;
using Hangfire;
using Hangfire.Common;
using Hangfire.States;

namespace DocumentOCR.IntegrationTests;

/// <summary>
/// Test double for <see cref="IBackgroundJobClient"/>. The real Hangfire client needs a
/// live job storage backend (Postgres in this app); tests instead capture what would have
/// been enqueued so the pipeline can be driven synchronously and deterministically.
/// </summary>
public sealed class RecordingBackgroundJobClient : IBackgroundJobClient
{
    private readonly ConcurrentBag<Guid> _enqueuedDocumentIds = [];

    public IReadOnlyCollection<Guid> EnqueuedDocumentIds => _enqueuedDocumentIds.ToList();

    public string Create(Job job, IState state)
    {
        if (job.Args.Count > 0 && job.Args[0] is Guid documentId)
            _enqueuedDocumentIds.Add(documentId);

        return Guid.NewGuid().ToString();
    }

    public bool ChangeState(string jobId, IState state, string expectedState) => true;
}
