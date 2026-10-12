#if IOS || MACCATALYST
using Foundation;
using ObjCRuntime;
using System.Runtime.InteropServices;
using UIKit;

namespace RichEdit.Maui
{
    public partial class RichEditorHandler
    {
        // UIKit can drop custom attributed-string keys when inserting an item,
        // but retains its NSTextList. Keep the projection data with that owner.
        private sealed class NativeListMetadata(RichTextListFormat format, double firstLineIndent, double textIndent) : NSObject
        {
            internal RichTextListFormat Format { get; } = format;
            internal double FirstLineIndent { get; } = firstLineIndent;
            internal double TextIndent { get; } = textIndent;
        }

        [LibraryImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_setAssociatedObject")]
        private static partial void SetAssociatedListMetadata(nint list, nint key, nint metadata, nuint policy);

        [LibraryImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_getAssociatedObject")]
        private static partial nint GetAssociatedListMetadata(nint list, nint key);

        private static NativeListMetadata? GetNativeListMetadata(NSParagraphStyle style)
        {
            if ((!OperatingSystem.IsIOSVersionAtLeast(16) && !OperatingSystem.IsMacCatalystVersionAtLeast(16)) ||
                style.TextLists is not { Length: > 0 } lists)
                return null;

            return Runtime.GetNSObject<NativeListMetadata>(GetAssociatedListMetadata(lists[^1].Handle, ParagraphMetadataKey.Handle));
        }

        private static double GetNativeFirstLineIndent(RichTextParagraphFormat format)
        {
            if (format.NativeList is null)
                return format.LeadingIndent + format.FirstLineIndent;

            var tab = format.TabStops.FirstOrDefault(tab => tab.Alignment == RichTextTabAlignment.Left)?.Position ?? 0;
            return tab > 0 ? tab : format.LeadingIndent;
        }

    }
}
#endif
