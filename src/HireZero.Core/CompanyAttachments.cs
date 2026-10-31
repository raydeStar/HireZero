using System.Text;

namespace HireZero.Core;

public sealed partial class CompanyStore
{
    private static CompanyAttachment[]? ConversationAttachments(CompanyCommand command, CompanyThread thread)
    {
        if (command.Attachments is not { Length: > 0 } files)
            return null;
        if (thread.Scope != "employee" || command.Skill is not (null or "chat"))
            throw new WorkspaceException("Attach files to a private employee conversation.", 422);
        if (files.Length > 3)
            throw new WorkspaceException("Attach up to three files per message.", 422);
        var result = new List<CompanyAttachment>();
        var bytes = 0;
        foreach (var file in files)
        {
            if (
                file is null
                || string.IsNullOrWhiteSpace(file.Name)
                || file.Name.Length > 160
                || file.Name.IndexOfAny(['/', '\\', ':']) >= 0
                || file.Kind is not ("document" or "image-ocr")
            )
                throw new WorkspaceException(
                    "Use a filename without a folder path and a supported attachment type.",
                    422
                );
            var name = Text(file.Name, 160);
            var text = Text(file.Text, 6000);
            bytes += Encoding.UTF8.GetByteCount(text);
            if (bytes > 6000)
                throw new WorkspaceException(
                    "Keep attached text within 6,000 UTF-8 bytes. Select the relevant passages before preparing a reply.",
                    422
                );
            result.Add(new(name, text, file.Kind));
        }
        return result.ToArray();
    }
}
