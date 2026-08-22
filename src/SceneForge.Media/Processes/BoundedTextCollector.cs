using System.Text;

namespace SceneForge.Media.Processes;

// Bounds captured process output to a fixed character budget so a runaway or
// malicious child process can never grow this buffer without limit
// (CLAUDE.md rule 6). The budget is measured in UTF-16 characters rather than
// exact UTF-8 bytes, which is a conservative approximation for the
// overwhelmingly ASCII output ffmpeg/ffprobe produce.
internal sealed class BoundedTextCollector
{
    private readonly StringBuilder _builder = new();
    private readonly int _maxCharacters;
    private bool _truncated;

    public BoundedTextCollector(int maxCharacters)
    {
        _maxCharacters = maxCharacters;
    }

    public void AppendLine(string line)
    {
        if (_truncated)
        {
            return;
        }

        if (_builder.Length + line.Length + 1 > _maxCharacters)
        {
            _builder.Append("...[truncated]");
            _truncated = true;
            return;
        }

        _builder.Append(line).Append('\n');
    }

    public override string ToString() => _builder.ToString();
}
