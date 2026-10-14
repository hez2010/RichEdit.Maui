namespace RichEdit.Core.Rtf;

// RTF-J permits a raw DBCS lead/trail pair. Its trailing byte is text even
// when the byte would otherwise be an RTF delimiter (RTF 1.9.1, pp. 200–201).
internal static class RtfDbcs
{
    internal static bool IsLead(int value, int codePage) => codePage switch
    {
        932 or 10001 => value is >= 0x81 and <= 0x9f or >= 0xe0 and <= 0xfc,
        936 or 949 or 950 or 10002 or 10003 or 10008 => value is >= 0x81 and <= 0xfe,
        1361 => value is >= 0x84 and <= 0xd3 or >= 0xd8 and <= 0xde or >= 0xe0 and <= 0xf9,
        _ => false,
    };

    internal static bool IsTrail(int value, int codePage) => codePage switch
    {
        932 or 10001 => value is >= 0x40 and <= 0xfc && value != 0x7f,
        936 or 10008 => value is >= 0x40 and <= 0xfe && value != 0x7f,
        949 or 10003 => value is >= 0x41 and <= 0x5a or >= 0x61 and <= 0x7a or >= 0x81 and <= 0xfe,
        950 or 10002 => value is >= 0x40 and <= 0x7e or >= 0xa1 and <= 0xfe,
        1361 => value is >= 0x31 and <= 0x7e or >= 0x81 and <= 0xfe,
        _ => false,
    };
}
