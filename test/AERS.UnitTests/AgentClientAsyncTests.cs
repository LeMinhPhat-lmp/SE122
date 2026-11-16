using System.Text.Json;
using AERS.AgentClient;
using Xunit;

namespace AERS.UnitTests;

public sealed class AgentClientAsyncTests
{
  [Fact]
  public async Task FileCredentialStore_RoundTripsAndReturnsNullForCorruptFiles()
  {
    var directory = Path.Combine(Path.GetTempPath(), $"aers-client-{Guid.NewGuid():N}");
    try
    {
      var store = new FileCredentialStore(directory);
      var credential = new StoredCredential(Guid.NewGuid(), "secret-key");
      await store.SaveAsync("agent-a", credential);

      Assert.Equal(credential, await store.LoadAsync("agent-a"));

      var file = Directory.GetFiles(directory).Single();
      await File.WriteAllTextAsync(file, "{not-json");
      Assert.Null(await store.LoadAsync("agent-a"));
    }
    finally
    {
      if (Directory.Exists(directory))
        Directory.Delete(directory, recursive: true);
    }
  }

  [Fact]
  public async Task FileCredentialStore_MissingAndEmptyFilesReturnNull()
  {
    var directory = Path.Combine(Path.GetTempPath(), $"aers-client-{Guid.NewGuid():N}");
    try
    {
      var store = new FileCredentialStore(directory);
      Assert.Null(await store.LoadAsync("missing"));
      await store.SaveAsync("empty", new StoredCredential(Guid.Empty, "key"));
      var file = Directory.GetFiles(directory).Single();
      await File.WriteAllTextAsync(file, string.Empty);
      Assert.Null(await store.LoadAsync("empty"));
    }
    finally
    {
      if (Directory.Exists(directory))
        Directory.Delete(directory, recursive: true);
    }
  }

  [Fact]
  public async Task TaskHandle_WorkingEventIsRaisedOnceAndTimeoutDoesNotCompleteIt()
  {
    var handle = new TaskHandle(Guid.NewGuid(), "submitted");
    var workingEvents = 0;
    handle.Working += (_, _) => workingEvents++;

    await Assert.ThrowsAsync<TimeoutException>(() => handle.WaitAsync(TimeSpan.FromMilliseconds(10)));
    handle.RaiseWorking();
    handle.RaiseWorking();
    var outcome = new TaskOutcome("completed", JsonDocument.Parse("{}").RootElement.Clone(), null);
    Assert.True(handle.TryComplete(outcome));

    Assert.Equal(1, workingEvents);
    Assert.Equal("completed", (await handle.Completion).Status);
  }
}
