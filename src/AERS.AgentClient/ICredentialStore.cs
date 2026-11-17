namespace AERS.AgentClient;

public interface ICredentialStore
{
  Task<StoredCredential?> LoadAsync(string agentKey, CancellationToken cancellationToken = default);
  Task SaveAsync(string agentKey, StoredCredential credential, CancellationToken cancellationToken = default);
  Task DeleteAsync(string agentKey, CancellationToken cancellationToken = default);
}
