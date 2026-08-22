namespace SceneForge.Media.Processes;

public enum ProcessOutputChannel
{
    StandardOutput,
    StandardError,
}

public readonly record struct ProcessOutputLine(ProcessOutputChannel Channel, string Text);
