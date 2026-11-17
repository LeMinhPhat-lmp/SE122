namespace AERS.AgentClient;

public sealed class NotConnectedException()
  : InvalidOperationException("The agent connection is not connected.");
