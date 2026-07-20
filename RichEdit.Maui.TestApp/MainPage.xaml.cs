using System.Globalization;
using RichEdit.Maui;

namespace RichEdit.Maui.TestApp;

public partial class MainPage : ContentPage
{
    private static readonly Color ActiveToolbarColor = Color.FromArgb("#6750A4");
    private static readonly Color InactiveToolbarColor = Color.FromArgb("#EDE9F7");
    private static readonly Color InactiveToolbarTextColor = Color.FromArgb("#2C2440");
    private static readonly RichTextListDefinition BulletedList = new(
    [
        new RichTextListLevelDefinition
        {
            Marker = new RichTextListMarker.Bullet("•"),
            Prefix = string.Empty,
            Suffix = string.Empty,
            LeadingIndent = 18,
            FirstLineIndent = -18,
            MarkerTab = 18,
        },
        new RichTextListLevelDefinition
        {
            Marker = new RichTextListMarker.Bullet("◦"),
            Prefix = string.Empty,
            Suffix = string.Empty,
            LeadingIndent = 36,
            FirstLineIndent = -18,
            MarkerTab = 36,
        },
        new RichTextListLevelDefinition
        {
            Marker = new RichTextListMarker.Bullet("▪"),
            Prefix = string.Empty,
            Suffix = string.Empty,
            LeadingIndent = 54,
            FirstLineIndent = -18,
            MarkerTab = 54,
        },
    ]);
    private static readonly RichTextListDefinition NumberedList = new(
    [
        new RichTextListLevelDefinition
        {
            Marker = new RichTextListMarker.Number(RichTextListNumberStyle.Arabic, 1),
            Prefix = string.Empty,
            Suffix = ".",
            LeadingIndent = 36,
            FirstLineIndent = -36,
            MarkerTab = 36,
        },
        new RichTextListLevelDefinition
        {
            Marker = new RichTextListMarker.Number(RichTextListNumberStyle.LowerLetter, 1),
            Prefix = string.Empty,
            Suffix = ")",
            LeadingIndent = 54,
            FirstLineIndent = -36,
            MarkerTab = 54,
        },
        new RichTextListLevelDefinition
        {
            Marker = new RichTextListMarker.Number(RichTextListNumberStyle.LowerRoman, 1),
            Prefix = "(",
            Suffix = ")",
            LeadingIndent = 72,
            FirstLineIndent = -36,
            MarkerTab = 72,
        },
    ]);
    private RichTextImage? _sampleImage;
    private bool _updatingToolbar;

    public MainPage()
    {
        InitializeComponent();

        FontPicker.ItemsSource = new[] { "Default", "Arial", "Courier New", "Georgia" };
        SizePicker.ItemsSource = new[] { "14", "17", "20", "24", "30" };

        _updatingToolbar = true;
        FontPicker.SelectedIndex = 0;
        SizePicker.SelectedIndex = 1;
        _updatingToolbar = false;

        Editor.Document = RichTextDocument.FromRtf("""
            {\rtf1\ansi\ansicpg1252\uc1 \deff0\deflang1033\deflangfe1033{\fonttbl{\f0\froman\fcharset0\fprq2{\*\panose 02020603050405020304}Times New Roman;}{\f3\froman\fcharset2\fprq2{\*\panose 05050102010706020507}Symbol;}
            {\f6\fmodern\fcharset0\fprq1{\*\panose 00000000000000000000}Courier;}{\f10\froman\fcharset0\fprq2{\*\panose 00000000000000000000}MS Serif;}{\f11\fswiss\fcharset0\fprq2{\*\panose 00000000000000000000}MS Sans Serif;}
            {\f14\fnil\fcharset2\fprq2{\*\panose 05000000000000000000}Wingdings;}{\f37\froman\fcharset238\fprq2 Times New Roman CE;}{\f38\froman\fcharset204\fprq2 Times New Roman Cyr;}{\f40\froman\fcharset161\fprq2 Times New Roman Greek;}
            {\f41\froman\fcharset162\fprq2 Times New Roman Tur;}{\f42\froman\fcharset186\fprq2 Times New Roman Baltic;}}{\colortbl;\red0\green0\blue0;\red0\green0\blue255;\red0\green255\blue255;\red0\green255\blue0;\red255\green0\blue255;\red255\green0\blue0;
            \red255\green255\blue0;\red255\green255\blue255;\red0\green0\blue128;\red0\green128\blue128;\red0\green128\blue0;\red128\green0\blue128;\red128\green0\blue0;\red128\green128\blue0;\red128\green128\blue128;\red192\green192\blue192;}{\stylesheet{
            \nowidctlpar\widctlpar\adjustright \fs20\cgrid \snext0 Normal;}{\s1\keepn\nowidctlpar\widctlpar\outlinelevel0\adjustright \b\fs28\lang1038\cgrid \sbasedon0 \snext0 heading 1;}{\s2\keepn\nowidctlpar\widctlpar\outlinelevel1\adjustright \fs44\lang1038\cgrid
            \sbasedon0 \snext0 heading 2;}{\s3\keepn\nowidctlpar\widctlpar\outlinelevel2\adjustright \cgrid \sbasedon0 \snext0 heading 3;}{\*\cs10 \additive Default Paragraph Font;}{\s15\qr\nowidctlpar\widctlpar\adjustright \cgrid \sbasedon0 \snext15 Body Text;}{
            \s16\qj\nowidctlpar\widctlpar\adjustright \cgrid \sbasedon0 \snext16 Body Text 2;}}{\*\listtable{\list\listtemplateid67698689\listsimple{\listlevel\levelnfc23\leveljc0\levelfollow0\levelstartat1\levelspace0\levelindent0{\leveltext
            \'01\u-3913 ?;}{\levelnumbers;}\f3\fbias0 \fi-360\li360\jclisttab\tx360 }{\listname ;}\listid707729170}{\list\listtemplateid67698707\listsimple{\listlevel\levelnfc1\leveljc0\levelfollow0\levelstartat1\levelspace0\levelindent0{\leveltext
            \'02\'00.;}{\levelnumbers\'01;}\fi-720\li720\jclisttab\tx720 }{\listname ;}\listid1026977971}{\list\listtemplateid67698689\listsimple{\listlevel\levelnfc23\leveljc0\levelfollow0\levelstartat1\levelspace0\levelindent0{\leveltext
            \'01\u-3913 ?;}{\levelnumbers;}\f3\fbias0 \fi-360\li360\jclisttab\tx360 }{\listname ;}\listid1434134974}}{\*\listoverridetable{\listoverride\listid1434134974\listoverridecount0\ls1}{\listoverride\listid707729170\listoverridecount0\ls2}
            {\listoverride\listid1026977971\listoverridecount0\ls3}}{\info{\title It is an example test rtf-file to RTF2XML bean for testing}{\author kissj}{\operator L\'e1szl\'f3 Zsolt Varga}{\creatim\yr2000\mo4\dy17\hr15\min34}{\revtim\yr2000\mo4\dy19\hr9\min34}
            {\version6}{\edmins7}{\nofpages2}{\nofwords217}{\nofchars1240}{\*\company MTA SZTAKI}{\nofcharsws0}{\vern71}}\widowctrl\ftnbj\aenddoc\hyphcaps0\formshade\viewkind1\viewscale100\pgbrdrhead\pgbrdrfoot \fet0\sectd \linex0\endnhere\sectdefaultcl {\*\pnseclvl1
            \pnucrm\pnstart1\pnindent720\pnhang{\pntxta .}}{\*\pnseclvl2\pnucltr\pnstart1\pnindent720\pnhang{\pntxta .}}{\*\pnseclvl3\pndec\pnstart1\pnindent720\pnhang{\pntxta .}}{\*\pnseclvl4\pnlcltr\pnstart1\pnindent720\pnhang{\pntxta )}}{\*\pnseclvl5
            \pndec\pnstart1\pnindent720\pnhang{\pntxtb (}{\pntxta )}}{\*\pnseclvl6\pnlcltr\pnstart1\pnindent720\pnhang{\pntxtb (}{\pntxta )}}{\*\pnseclvl7\pnlcrm\pnstart1\pnindent720\pnhang{\pntxtb (}{\pntxta )}}{\*\pnseclvl8\pnlcltr\pnstart1\pnindent720\pnhang
            {\pntxtb (}{\pntxta )}}{\*\pnseclvl9\pnlcrm\pnstart1\pnindent720\pnhang{\pntxtb (}{\pntxta )}}\pard\plain \s1\keepn\nowidctlpar\widctlpar\outlinelevel0\adjustright \b\fs28\lang1038\cgrid {It is an example test rtf-file to RTF2XML bean for testing
            \par }\pard\plain \nowidctlpar\widctlpar\adjustright \fs20\cgrid {\b\fs28\lang1038
            \par }{\lang1038 Font size 10, plain text;
            \par }{\b\fs24\lang1038 Font size 12, bold text. }{\b\fs24\ul\lang1038 Underline,bold text.
            \par  }{\b\i\fs24\ul\lang1038 Underline,italic,bold text.}{\lang1038
            \par }\pard\plain \s2\keepn\nowidctlpar\widctlpar\outlinelevel1\adjustright \fs44\lang1038\cgrid {Font size 22, plain text.
            \par }\pard\plain \nowidctlpar\widctlpar\adjustright \fs20\cgrid {                                                 }{\b\fs44 Bold text.
            \par \tab \tab \tab }{       }{\i\fs44 Italic text.
            \par }{\fs24
            \par    Simple table :
            \par
            \par
            \par }\trowd \trgaph108\trrh492\trleft-45\trbrdrt\brdrs\brdrw10 \trbrdrl\brdrs\brdrw10 \trbrdrb\brdrs\brdrw10 \trbrdrr\brdrs\brdrw10 \trbrdrh\brdrs\brdrw10 \trbrdrv\brdrs\brdrw10 \clvertalt\clbrdrt\brdrs\brdrw30 \clbrdrl\brdrs\brdrw30 \clbrdrb\brdrs\brdrw30
            \clbrdrr\brdrs\brdrw30 \cltxlrtb \cellx1449\clvertalt\clbrdrt\brdrs\brdrw30 \clbrdrb\brdrs\brdrw30 \clbrdrr\brdrs\brdrw30 \cltxlrtb \cellx2943\clvertalt\clbrdrt\brdrs\brdrw30 \clbrdrb\brdrs\brdrw30 \clbrdrr\brdrs\brdrw30 \cltxlrtb \cellx4437\clvertalt
            \clbrdrt\brdrs\brdrw30 \clbrdrb\brdrs\brdrw30 \clbrdrr\brdrs\brdrw30 \cltxlrtb \cellx5931\clvertalt\clbrdrt\brdrs\brdrw30 \clbrdrb\brdrs\brdrw30 \clbrdrr\brdrs\brdrw30 \cltxlrtb \cellx7425\pard \nowidctlpar\widctlpar\intbl\adjustright {\fs24 1}{
            \fs24\super st}{\fs24  column\cell 2}{\fs24\super nd}{\fs24  column\cell 3}{\fs24\super rd}{\fs24  column\cell 4}{\fs24\super th}{\fs24  column\cell 5}{\fs24\super th}{\fs24  column\cell }\pard \nowidctlpar\widctlpar\intbl\adjustright {\fs24 \row }\trowd
            \trgaph108\trrh489\trleft-45\trbrdrt\brdrs\brdrw10 \trbrdrl\brdrs\brdrw10 \trbrdrb\brdrs\brdrw10 \trbrdrr\brdrs\brdrw10 \trbrdrh\brdrs\brdrw10 \trbrdrv\brdrs\brdrw10 \clvertalt\clbrdrt\brdrs\brdrw30 \clbrdrl\brdrs\brdrw30 \clbrdrb\brdrs\brdrw10 \clbrdrr
            \brdrs\brdrw30 \cltxlrtb \cellx1449\clvertalt\clbrdrt\brdrs\brdrw30 \clbrdrb\brdrs\brdrw10 \clbrdrr\brdrs\brdrw30 \cltxlrtb \cellx2943\clvertalt\clbrdrt\brdrs\brdrw30 \clbrdrb\brdrs\brdrw10 \clbrdrr\brdrs\brdrw30 \cltxlrtb \cellx4437\clvertalt\clbrdrt
            \brdrs\brdrw30 \clbrdrb\brdrs\brdrw10 \clbrdrr\brdrs\brdrw30 \cltxlrtb \cellx5931\clvertalt\clbrdrt\brdrs\brdrw30 \clbrdrb\brdrs\brdrw10 \clbrdrr\brdrs\brdrw30 \cltxlrtb \cellx7425\pard \nowidctlpar\widctlpar\intbl\adjustright {\fs24 1.1 item\cell
            1.2 item\cell 1.3 item\cell 1.4 item\cell 1.5 item\cell }\pard \nowidctlpar\widctlpar\intbl\adjustright {\fs24 \row }\trowd \trgaph108\trrh489\trleft-45\trbrdrt\brdrs\brdrw10 \trbrdrl\brdrs\brdrw10 \trbrdrb\brdrs\brdrw10 \trbrdrr\brdrs\brdrw10 \trbrdrh
            \brdrs\brdrw10 \trbrdrv\brdrs\brdrw10 \clvertalt\clbrdrt\brdrs\brdrw10 \clbrdrl\brdrs\brdrw30 \clbrdrb\brdrs\brdrw10 \clbrdrr\brdrs\brdrw30 \cltxlrtb \cellx1449\clvertalt\clbrdrt\brdrs\brdrw10 \clbrdrb\brdrs\brdrw10 \clbrdrr\brdrs\brdrw30 \cltxlrtb
            \cellx2943\clvertalt\clbrdrt\brdrs\brdrw10 \clbrdrb\brdrs\brdrw10 \clbrdrr\brdrs\brdrw30 \cltxlrtb \cellx4437\clvertalt\clbrdrt\brdrs\brdrw10 \clbrdrb\brdrs\brdrw10 \clbrdrr\brdrs\brdrw30 \cltxlrtb \cellx5931\clvertalt\clbrdrt\brdrs\brdrw10 \clbrdrb
            \brdrs\brdrw10 \clbrdrr\brdrs\brdrw30 \cltxlrtb \cellx7425\pard \nowidctlpar\widctlpar\intbl\adjustright {\b\fs24 2.1 item\cell 2.2 item\cell 2.3 item\cell 2.4 item\cell 2.5 item\cell }\pard \nowidctlpar\widctlpar\intbl\adjustright {\fs24 \row }\pard
            \nowidctlpar\widctlpar\intbl\adjustright {\b\i\fs24 3.1 item\cell 3.2 item\cell 3.3 item\cell 3.4 item\cell 3.5 item\cell }\pard \nowidctlpar\widctlpar\intbl\adjustright {\fs24 \row }\pard \nowidctlpar\widctlpar\intbl\adjustright {\b\fs24\ul 4.1 item
            \cell 4.2 item\cell 4.3 item\cell 4.4 item\cell 4.5 item\cell }\pard \nowidctlpar\widctlpar\intbl\adjustright {\fs24 \row }\pard \nowidctlpar\widctlpar\intbl\adjustright {\b\i\fs24\ul 5.1 item\cell 5.2 item\cell 5.3 item\cell 5.4 item\cell 5.5 item\cell
            }\pard \nowidctlpar\widctlpar\intbl\adjustright {\fs24 \row }\pard\plain \s3\keepn\nowidctlpar\widctlpar\intbl\outlinelevel2\adjustright \cgrid {Empty \cell }\pard\plain \nowidctlpar\widctlpar\intbl\adjustright \fs20\cgrid {\fs24 \'85\cell \'85\cell \'85
            \cell Empty\cell }\pard \nowidctlpar\widctlpar\intbl\adjustright {\fs24 \row }\trowd \trgaph108\trrh489\trleft-45\trbrdrt\brdrs\brdrw10 \trbrdrl\brdrs\brdrw10 \trbrdrb\brdrs\brdrw10 \trbrdrr\brdrs\brdrw10 \trbrdrh\brdrs\brdrw10 \trbrdrv\brdrs\brdrw10
            \clvertalt\clbrdrt\brdrs\brdrw10 \clbrdrl\brdrs\brdrw30 \clbrdrb\brdrs\brdrw30 \clbrdrr\brdrs\brdrw30 \cltxlrtb \cellx1449\clvertalt\clbrdrt\brdrs\brdrw10 \clbrdrb\brdrs\brdrw30 \clbrdrr\brdrs\brdrw30 \cltxlrtb \cellx2943\clvertalt\clbrdrt\brdrs\brdrw10
            \clbrdrb\brdrs\brdrw30 \clbrdrr\brdrs\brdrw30 \cltxlrtb \cellx4437\clvertalt\clbrdrt\brdrs\brdrw10 \clbrdrb\brdrs\brdrw30 \clbrdrr\brdrs\brdrw30 \cltxlrtb \cellx5931\clvertalt\clbrdrt\brdrs\brdrw10 \clbrdrb\brdrs\brdrw30 \clbrdrr\brdrs\brdrw30 \cltxlrtb
            \cellx7425\pard \nowidctlpar\widctlpar\intbl\adjustright {\fs24 Last items\cell \'85\cell \'85\cell \'85\cell Last items\cell }\pard \nowidctlpar\widctlpar\intbl\adjustright {\fs24 \row }\pard \nowidctlpar\widctlpar\adjustright {\fs24
            \par
            \par List :
            \par
            \par {\pntext\pard\plain\f3\cgrid \loch\af3\dbch\af0\hich\f3 \'b7\tab}}\pard \fi-360\li360\nowidctlpar\widctlpar\jclisttab\tx360{\*\pn \pnlvlblt\ilvl0\ls2\pnrnot0\pnf3\pnstart1\pnindent360\pnhang{\pntxtb \'b7}}\ls2\adjustright {\fs24 It is the 1}{\fs24\super
            st}{\fs24  row of the list
            \par {\pntext\pard\plain\f3\cgrid \loch\af3\dbch\af0\hich\f3 \'b7\tab}}\pard \fi-360\li360\nowidctlpar\widctlpar\jclisttab\tx360{\*\pn \pnlvlblt\ilvl0\ls2\pnrnot0\pnf3\pnstart1\pnindent360\pnhang{\pntxtb \'b7}}\ls2\adjustright {\fs24 It is the 2}{\fs24\super
            nd}{\fs24  row of the list
            \par {\pntext\pard\plain\f3\cgrid \loch\af3\dbch\af0\hich\f3 \'b7\tab}}\pard \fi-360\li360\nowidctlpar\widctlpar\jclisttab\tx360{\*\pn \pnlvlblt\ilvl0\ls2\pnrnot0\pnf3\pnstart1\pnindent360\pnhang{\pntxtb \'b7}}\ls2\adjustright {\fs24 \'85
            \par {\pntext\pard\plain\f3\cgrid \loch\af3\dbch\af0\hich\f3 \'b7\tab}}\pard \fi-360\li360\nowidctlpar\widctlpar\jclisttab\tx360{\*\pn \pnlvlblt\ilvl0\ls2\pnrnot0\pnf3\pnstart1\pnindent360\pnhang{\pntxtb \'b7}}\ls2\adjustright {\fs24 \'85
            \par {\pntext\pard\plain\f3\cgrid \loch\af3\dbch\af0\hich\f3 \'b7\tab}}\pard \fi-360\li360\nowidctlpar\widctlpar\jclisttab\tx360{\*\pn \pnlvlblt\ilvl0\ls2\pnrnot0\pnf3\pnstart1\pnindent360\pnhang{\pntxtb \'b7}}\ls2\adjustright {\fs24 \'85
            \par {\pntext\pard\plain\f3\cgrid \loch\af3\dbch\af0\hich\f3 \'b7\tab}}\pard \fi-360\li360\nowidctlpar\widctlpar\jclisttab\tx360{\*\pn \pnlvlblt\ilvl0\ls2\pnrnot0\pnf3\pnstart1\pnindent360\pnhang{\pntxtb \'b7}}\ls2\adjustright {\fs24
            It is the last row of the list
            \par }\pard \nowidctlpar\widctlpar\adjustright {\fs24
            \par }{\f6\fs24  Here is a brief Courier text.
            \par }{\f11\fs24   Here is a brief MS Sans - Serif text.
            \par   }{\f10\fs24 Here is a brief MS Serif text.
            \par   }{\fs24 Here is a brief Times New Roman text.
            \par
            \par
            \par
            \par  }{\fs24\ul Some paragraphs}{\fs24  :
            \par
            \par {\pntext\pard\plain\cgrid \hich\af0\dbch\af0\loch\f0 I.\tab}}\pard \fi-720\li720\nowidctlpar\widctlpar\jclisttab\tx720{\*\pn \pnlvlbody\ilvl0\ls3\pnrnot0\pnucrm\pnstart1\pnindent720\pnhang{\pntxta .}}\ls3\adjustright {\fs24 Align left :
            \par }\pard \nowidctlpar\widctlpar{\*\pn \pnlvlcont\ilvl0\ls0\pnrnot0\pndec }\adjustright {\fs24
            \par      The text you are reading is aligned left. It is an align \endash  left text. It is also an align \endash  left sentence.
            \par
            \par {\pntext\pard\plain\cgrid \hich\af0\dbch\af0\loch\f0 II.\tab}}\pard \fi-720\li720\nowidctlpar\widctlpar\jclisttab\tx720{\*\pn \pnlvlbody\ilvl0\ls3\pnrnot0\pnucrm\pnstart1\pnindent720\pnhang{\pntxta .}}\ls3\adjustright {\fs24 Align right:
            \par }\pard \nowidctlpar\widctlpar{\*\pn \pnlvlcont\ilvl0\ls0\pnrnot0\pndec }\adjustright {\fs24
            \par }\pard\plain \s15\qr\nowidctlpar\widctlpar{\*\pn \pnlvlcont\ilvl0\ls0\pnrnot0\pndec }\adjustright \cgrid {  The text you are reading is aligned right. It is an align \endash  right text. It is also an align \endash  right sentence.
            \par }\pard\plain \nowidctlpar\widctlpar{\*\pn \pnlvlcont\ilvl0\ls0\pnrnot0\pndec }\adjustright \fs20\cgrid {\fs24
            \par {\pntext\pard\plain\cgrid \hich\af0\dbch\af0\loch\f0 III.\tab}}\pard \fi-720\li720\nowidctlpar\widctlpar\jclisttab\tx720{\*\pn \pnlvlbody\ilvl0\ls3\pnrnot0\pnucrm\pnstart1\pnindent720\pnhang{\pntxta .}}\ls3\adjustright {\fs24 Align centered:
            \par }\pard \nowidctlpar\widctlpar{\*\pn \pnlvlcont\ilvl0\ls0\pnrnot0\pndec }\adjustright {\fs24
            \par }\pard \qc\nowidctlpar\widctlpar{\*\pn \pnlvlcont\ilvl0\ls0\pnrnot0\pndec }\adjustright {\fs24         The text you are reading is aligned center. It is an align \endash  centered text. It is also an align \endash  centered sentence.
            \par
            \par {\pntext\pard\plain\cgrid \hich\af0\dbch\af0\loch\f0 IV.\tab}}\pard \fi-720\li720\nowidctlpar\widctlpar\jclisttab\tx720{\*\pn \pnlvlbody\ilvl0\ls3\pnrnot0\pnucrm\pnstart1\pnindent720\pnhang{\pntxta .}}\ls3\adjustright {\fs24 Align justified:
            \par }\pard \nowidctlpar\widctlpar\adjustright {\fs24
            \par }\pard\plain \s16\qj\nowidctlpar\widctlpar\adjustright \cgrid {          The text you are reading is aligned justify. It is an align \endash  justified text. It is also an align \endash  justified sentence.
            \par
            \par }{\b Here are some special characters:}{ \'f6t }{\f37 \'e1rv\'edzt\'fbr\'f5 \'fctvef\'far\'f3g\'e9p, which means \ldblquote }{five flood resistant hammer drills\rdblquote  (}{{\field{\*\fldinst SYMBOL 74 \\f "Wingdings" \\s 12}{\fldrslt\f14\fs24}}}{
            ) in Hungarian.
            \par }\pard\plain \qj\nowidctlpar\widctlpar\adjustright \fs20\cgrid {\fs24
            \par    At last you can see an image :
            \par
            \par }{\pict\pngblip\picw210\pich105\picwgoal3150\pichgoal1575
            89504e470d0a1a0a0000000d49484452000000d20000006908060000004f947df4000000017352474200aece1ce90000000467414d410000b18f0bfc61050000
            00097048597300000ec300000ec301c76fa86400000bf249444154785eed9d099054c51dc69b9b78804110394a20042d8e18252845145014cf0a521691a448a1
            78a4bc502bbaec313bdbef9c9dd90b51901004045320478118404044d81539144517d470ac200aca252a1116427fa97ecec2f2de2ceec81176e7fb55fd6aa9da
            9d65a7e7ff4df7ebe9ee27060e9cbd65d8b0097be971870c99b9f7bcf30e8c15845497cd9b3b1e0004e871956a8b366db64ff5b7152155525ada6d9fbf9052dd
            bd7b3ba175eb2fa6f8db8a902a6190823248246918a4a0fbf65d8e962dbf1eef6f2b42aa84410aba63c765b8e69a356596953d554ae9e938ce54c330c64e9a34
            a9b1bf0d09619012f8f9e7add1af5f090a0b2388c5629e454545300ce3f0a851a39af8db9010062981dbb7b741dfbecb60db611d1e4fc771f4d7af63b1d885fe
            362484414a2083449286410aca2091a46190823248246918a4a00c12491a06292883449286410aca2091a46190823248246918a4a00c12491a06292883449286
            410aca2091a46190823248246918a4a00c12491a06292883449286410aca2091a46190823248246918a4a00c12491a06292883449286410a7ab2203d3de5e9f3
            fd6d581908d4d72aa1da29a1aea8e46f9450f728a106c7bdd1f77ded85f1c7d7f3ff5e728ec32005ad1ca41c2b0759912c64178590959fb613100d2070413c28
            fd95500f40c0d82ad49cd542ad992d549976ac50071d01541811c0c302b82f6e76a5ef6973053053a81dfab1af0bb5f953a1e6fd20d40b4aa87425d41d4aa8ab
            9550adfcaf1f39476090827ebeab39badffc06720a9fc6a81c0bd31f1b83a58366e39dabdfffcf7b758fae9e23d417cf0b553e4200c3047093003a08a08900c4
            69b08e005a09a0a700060860b80062029820d4812542956e14ea75255428deabb5f6bfa6e4ff008314b4fc83ab30b2cbc758d9e35dbcda6c2fa202182a806e02
            f86582c23f9bd617405b010c144048005384fa7e8350254aa86c25541f3d34f4bfc6e42cc020c5dd7331f0e203f8ead6859872c1f77842005d1214f2b9e82502
            b84b00ae008a85fa440955a484ea0d81bafed79b9c21523e48cbfae2d0b089f8578b5df8f339d0e39caa8d047073fcba6bad50eb9450cf28a15af85f77729a49
            c920a93ac0b43f615bef62afe03a2528c8dae045f16bb8b942ed51428d544275f0bffee4349172415a7213b6f478178f09e08204c5575bd59316b384fa2e3eec
            6be9af03728aa44c90b6b7c5e1fb26791307a76b76ad26aa03b54ca82f21f0577f2d905320258234fd1ebc79e94e5c99a0b052d18602080b60bf507378fd749a
            a8d5413adc00786aa4374dec2f260adc2080d5426d5142f5f5d70549925a1ba4f286d87dfb026f06cb5f40f4b82d04f0b250e54aa85bfdb54192a05606e9685d
            ecba633e7e9ba07068503dd49b22d42125d42dfefa20d5a43606e9bf4f3e8b5b12140cad5afdf9d3dc1fc3d4d95f23a41ad4ba20cd1988f40485427f5abdf4e8
            c33aeadf105f9ee7af13f213d4aa20ed6f8a8fda7f86ba098a8456cf87ea00657d56af18307dc015fe5a2127a1560569fc83de624e7f71d0ea5b4f00ab5a9563
            b4e3eecbb0336ef6d70ba9825a13245507bbfb2f4ee90f5b4f97190258fae02bc82ecc3e180a8518a6eab06953a7da11a4c30d30a7d9de4051d0e4d5fba056f6
            5a819c67b3e1baeec17038dcd35f37c4c7ac5983515676ed31b76ce981f5ebbba2b4b4abf7b54658da0d5b5fbf0da31a1f0c14054ddee67a1951e78f21a3e9c8
            8de6c2b2acad52ca4bfdb5432ad1a041b9dda8d1c1820a9b35db3dbe77efe2a37dfb96a04f9fe21ae175372e45b85b29f48e557f51d0e4d5415ada6503642c1d
            524a141515212727679cbf76c849d8b245348d44328ec462d98844b26a84567e1aa6664431b66179a02868f2b6164049d7f5c889a57b07bf98a6a90f7f396a59
            d6d5fe7a2155100a853a188671c4b2ac6327e89cebea034a9e0bdb78b5c9b781a2a0c97ba700dee9bf18e9f9238eb571414181fe5ae8af175205353148daacbc
            1178ffaa75e89ca03068728ed27b971e198bcc48d6b1f6755d570ff3360e1f3ebc91bf664802a494bfb26d1bb9b9b95ee3d514730a7230ffd19730ba5eb03068
            f5d58b58d7b6db062b920569ca6341d2356118c67752cae6fe9a21099052b695527e6a59d626c3306a9439d1f44deb3b6cd9dd3d4181d0ea394600afdd3f01e9
            d18c137a7c7d9de4baae0a87c3bdfc3543aa404a59b7262a20eaa2dd67ed5708b5931fcc26af5e15f261af95c8c84f0b0c9df550df34cd0352caf6fe7a21b514
            7d70e23f853aa4b707f88b8526565f5baeef5086881b42d8ca0904490feda494fbd2d2d278f6792aa1f7d6e83d360cd34f7b9dde29db6a07c664b9def1ccfe10
            69a3d1a8fefa96d7f393d442effa9c2dd40fed13140ffd517d965f69d70ddec70719b99930a5190891362f2f4ff74843fd6d4c52047df8fcc7427da84fcff117
            512a7b617c9a7b9d3edbcf0d213312aa32447a06d7308ccde3c68d6be06f5f9242a85f6f6c52d6b574e3c40640cb0445956afe41002b2ede8385f74ef6261612
            5d1355a8271962b198ee8d06fadb95a42003565ddf65de43930f7dd0e94be4c4df91fd0556dbbd569fcd50f728d6f52ef1867223f28eaf5ca84abda2414a99e7
            6f4f92c20c1b37ecf6589e515e7cdf4c1437df85a7446af4507d0530b1ee51acedbe1653873f8f8cfc11c876b203a1f1ab17ab4a2917fadb9110916566dd9e55
            143a3c2612c3e2c1af60458732ef5ae1fa04055893d56f10fadcefd9e71fc0aaebdec6b4c7477bcba82a2ffba94a3d9ccbcfcfd753ded3a4948dfd6d4888870c
            c97e76d4fecc1c6dc28e6461e62363b1b2d73b9877d137deee50bd3242dfeccb5f9ce7ba7afb83fe50f51ff58fa0a4e3662cbb6b2ec666e67ad741554d6bfb8d
            4422dee482699a117fbb1112203333b3a56ddbafe517e47bebf432f3d2302a6c63ded0295879cd1a2c68b1cbbb5de5dd026897a068cf05cf17f08e617e480093
            1b96a3b8e36614dfba0893ff5604333713e97923905de93eb827532f01d2bd90e3385f87c3e1bbfced45c849310ce35edbb6777adb045c0319d1746f08546818
            98f5f0dfb1fccef958d175035e6dba1f79f1fbbfeaadd8fa569767eb6e16fa54a466f19e52cfb83da9af791a1dc292cbb6a1a4e76abcf1c799989096eff5ae3a
            3c213774c282d393a903a467e5f407aeb66d4fc8cacae27d6ac9cf232d2dad75241279ceb6ed03f17765e4983fde845917664e2cc39be59afef868bc39681696
            dff0164aaefc084bdb6dc5b4a6df624cfd23dea1f483e2ab04f4fd962aab7b0e7dcbca44ead5d697fb7efe3601dc2fe0cd2ebed4f820e6b7fc0a6f75da8815bf
            5f81e5772cc0fca15330f199026f398f0e7d7a2cdd9b3ca86e782a07480fe31cc759649a667f7fbb10f2b3b06dbba36ddbcfdbb6bd43072a7eade0155ec5ddcd
            75d17a9fbdc4d2e1ba218c0edb78f9a99198fbe08b5872f76cbcdda718cbaf5d1377354a7abce7f51c0bda6e4fe8f24e9bb0ace7aa4a8f5983925b1661d1e0e9
            98f9e80b5e4f53604a98d10c64ead0e48d40463423e9e054a8d7ccc52712b48b5dd76580c899414ad9cc719c272ccb7a4f179c5e1ea3f73c5584aa425dc83a60
            7a289519bf26d1c51ef2a97b8ea8939d50bdef272b3fed849fcfd0c6d2bde0fedcc054563f07ddfbe8e76159d61ed334c73b8ed3ddffbc0939635896f53bc771
            628ee36cd461d2efe6f1eb8940b0aa5207e164fa7ffe54d5d3d7fa6fd5c1d101b22ceb1bdbb6675996f51729e525fee748c859434ad9d034cd5e9665856ddb5e
            6a9ae67e3d55ac83a58b55ff3b99709d0ef5ff55111a3d0cad088e699acab6ed4f4dd39ce0baee20d77579ab4b726ee2ba6e0bd3346fb26ddbb06d7bae699a9f
            48297fd061d23d962e6aadfe77c576783d8911df207782fe705456ffbe8ac7eab0e8a05484373edc2c374d739b6559ef3a8ef39cebbaf7b8aedb65c68c19f5fc
            7f33213502c771da48297b442291018661840dc3186fdbf642c330ca0cc3d826a5fc4287a572982a866015a1ab1ca2f801347bf5632dcbd2be6d18c64c1ddef8
            30ad5f341abdacb0b0f017febf85905a87de7ea08786bae02dcbea6c5956d70af5b47324121962dbf61029e5a0cadf8b46a35df56123fab13366cc68e8ffbd84
            10420821841042082184104208218410420821841042082184104208218410420821841042082184104208218410420821841042082184104208218410420821
            84104208216796ff0163b8c7e213d20a240000000049454e44ae426082
            }{\fs24
            \par
            \par }}
            """);
        _sampleImage = Editor.Document.CurrentSnapshot.Images.FirstOrDefault();
        UpdateToolbar();
    }

    private void OnNewDocumentClicked(object? sender, EventArgs e) =>
        Editor.Document.Edit(edit =>
            edit.DeleteText(new RichTextRange(0, Editor.Document.Length)));

    private void OnBoldClicked(object? sender, EventArgs e)
    {
        Editor.Selection.ToggleBold();
        Editor.Focus();
    }

    private void OnItalicClicked(object? sender, EventArgs e)
    {
        Editor.Selection.ToggleItalic();
        Editor.Focus();
    }

    private void OnUnderlineClicked(object? sender, EventArgs e)
    {
        Editor.Selection.ToggleUnderline(RichTextUnderlineStyle.Single);
        Editor.Focus();
    }

    private void OnStrikethroughClicked(object? sender, EventArgs e)
    {
        Editor.Selection.ToggleStrikethrough(RichTextStrikethroughStyle.Single);
        Editor.Focus();
    }

    private void OnSuperscriptClicked(object? sender, EventArgs e)
    {
        Editor.Selection.ToggleScript(RichTextScript.Superscript);
        Editor.Focus();
    }

    private void OnSubscriptClicked(object? sender, EventArgs e)
    {
        Editor.Selection.ToggleScript(RichTextScript.Subscript);
        Editor.Focus();
    }

    private void OnBulletedListClicked(object? sender, EventArgs e)
    {
        Editor.Selection.ToggleList(BulletedList);
        Editor.Focus();
    }

    private void OnNumberedListClicked(object? sender, EventArgs e)
    {
        Editor.Selection.ToggleList(NumberedList);
        Editor.Focus();
    }

    private void OnClearListClicked(object? sender, EventArgs e)
    {
        Editor.Selection.ClearList();
        Editor.Focus();
    }

    private void OnOutdentListClicked(object? sender, EventArgs e)
    {
        Editor.Selection.ChangeListLevel(-1);
        Editor.Focus();
    }

    private void OnIndentListClicked(object? sender, EventArgs e)
    {
        Editor.Selection.ChangeListLevel(1);
        Editor.Focus();
    }

    private void OnRestartListClicked(object? sender, EventArgs e)
    {
        Editor.Selection.RestartList(1);
        Editor.Focus();
    }

    private void OnAlignLeftClicked(object? sender, EventArgs e) =>
        SetAlignment(RichTextAlignment.Left);

    private void OnAlignCenterClicked(object? sender, EventArgs e) =>
        SetAlignment(RichTextAlignment.Center);

    private void OnAlignRightClicked(object? sender, EventArgs e) =>
        SetAlignment(RichTextAlignment.Right);

    private void OnAlignJustifyClicked(object? sender, EventArgs e) =>
        SetAlignment(RichTextAlignment.Justified);

    private void SetAlignment(RichTextAlignment alignment)
    {
        Editor.Selection.ParagraphFormat.Alignment = alignment;
        Editor.Focus();
    }

    private void OnSetLinkClicked(object? sender, EventArgs e)
    {
        if (Editor.SelectedRange.IsEmpty)
        {
            StatusLabel.Text = "Select the link text first";
            return;
        }

        Editor.Selection.SetLink(
            "https://github.com/hez2010/RichEdit.Maui",
            "RichEdit.Maui repository");
        Editor.Focus();
    }

    private void OnRemoveLinksClicked(object? sender, EventArgs e)
    {
        Editor.Selection.RemoveLinks();
        Editor.Focus();
    }

    private void OnInsertImageClicked(object? sender, EventArgs e)
    {
        if (_sampleImage is null)
        {
            StatusLabel.Text = "The sample image is unavailable";
            return;
        }

        Editor.Selection.InsertImage(_sampleImage with { Position = 0 });
        Editor.Focus();
    }

    private void OnInsertFieldClicked(object? sender, EventArgs e)
    {
        Editor.Selection.InsertField(
            "DATE \\@ \"yyyy-MM-dd\"",
            DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        Editor.Focus();
    }

    private void OnFontChanged(object? sender, EventArgs e)
    {
        if (_updatingToolbar || FontPicker.SelectedItem is not string font)
        {
            return;
        }

        Editor.Selection.CharacterFormat.FontFamily = font == "Default" ? null : font;
        Editor.Focus();
    }

    private void OnSizeChanged(object? sender, EventArgs e)
    {
        if (_updatingToolbar || SizePicker.SelectedItem is not string value ||
            !double.TryParse(value, out var size))
        {
            return;
        }

        Editor.Selection.CharacterFormat.FontSize = size;
        Editor.Focus();
    }

    private void OnForegroundColorClicked(object? sender, EventArgs e)
    {
        if (sender is Button button)
        {
            Editor.Selection.CharacterFormat.ForegroundColor = GetCommandColor(button);
            Editor.Focus();
        }
    }

    private void OnBackgroundColorClicked(object? sender, EventArgs e)
    {
        if (sender is Button button)
        {
            Editor.Selection.CharacterFormat.BackgroundColor = GetCommandColor(button);
            Editor.Focus();
        }
    }

    private void OnEditorSelectionChanged(object? sender, RichTextSelectionChangedEventArgs e) =>
        UpdateToolbar();

    private void OnEditorSelectionFormatChanged(object? sender, EventArgs e) =>
        UpdateToolbar();

    private void OnEditorContentChanged(object? sender, RichTextContentChangedEventArgs e) =>
        UpdateToolbar();

    private void OnEditorPasting(object? sender, RichTextPastingEventArgs e) =>
        StatusLabel.Text = $"Pasting {e.Fragment.Text.Length} characters";

    private void OnEditorLinkInvoked(object? sender, RichTextLinkInvokedEventArgs e)
    {
        e.Handled = true;
        StatusLabel.Text = $"Link invoked: {e.Target}";
    }

    private void OnEditorInlineObjectInvoked(
        object? sender,
        RichTextInlineObjectInvokedEventArgs e)
    {
        e.Handled = true;
        StatusLabel.Text = $"Image invoked: {e.Image.AlternativeText ?? "inline image"}";
    }

    private async void OnCopyRtfClicked(object? sender, EventArgs e)
    {
        await Clipboard.Default.SetTextAsync(Editor.Document.RtfText);
        StatusLabel.Text = "RTF copied to the clipboard";
    }

    private void UpdateToolbar()
    {
        var characterFormat = Editor.Selection.CharacterFormat;
        var paragraphFormat = Editor.Selection.ParagraphFormat;
        var listMarker = GetSelectedListMarker(paragraphFormat);
        SetToolbarState(BoldButton, !characterFormat.IsBoldMixed && characterFormat.Bold);
        SetToolbarState(ItalicButton, !characterFormat.IsItalicMixed && characterFormat.Italic);
        SetToolbarState(
            UnderlineButton,
            !characterFormat.IsUnderlineMixed &&
            characterFormat.Underline != RichTextUnderlineStyle.None);
        SetToolbarState(
            StrikethroughButton,
            !characterFormat.IsStrikethroughMixed &&
            characterFormat.Strikethrough != RichTextStrikethroughStyle.None);
        SetToolbarState(
            SuperscriptButton,
            !characterFormat.IsScriptMixed &&
            characterFormat.Script == RichTextScript.Superscript);
        SetToolbarState(
            SubscriptButton,
            !characterFormat.IsScriptMixed &&
            characterFormat.Script == RichTextScript.Subscript);
        SetToolbarState(
            BulletButton,
            !paragraphFormat.IsListMixed &&
            listMarker is RichTextListMarker.Bullet or RichTextListMarker.Picture);
        SetToolbarState(
            NumberButton,
            !paragraphFormat.IsListMixed && listMarker is RichTextListMarker.Number);
        SetToolbarState(
            AlignLeftButton,
            !paragraphFormat.IsAlignmentMixed &&
            paragraphFormat.Alignment == RichTextAlignment.Left);
        SetToolbarState(
            AlignCenterButton,
            !paragraphFormat.IsAlignmentMixed &&
            paragraphFormat.Alignment == RichTextAlignment.Center);
        SetToolbarState(
            AlignRightButton,
            !paragraphFormat.IsAlignmentMixed &&
            paragraphFormat.Alignment == RichTextAlignment.Right);
        SetToolbarState(
            AlignJustifyButton,
            !paragraphFormat.IsAlignmentMixed &&
            paragraphFormat.Alignment is RichTextAlignment.Justified or
                RichTextAlignment.Distributed);
        SetColorState(ForegroundDefaultButton, characterFormat.ForegroundColor, null);
        SetColorState(ForegroundCoralButton, characterFormat.ForegroundColor, "#E06C75");
        SetColorState(ForegroundBlueButton, characterFormat.ForegroundColor, "#327CCB");
        SetColorState(ForegroundGreenButton, characterFormat.ForegroundColor, "#3A8F45");
        SetColorState(BackgroundClearButton, characterFormat.BackgroundColor, null);
        SetColorState(BackgroundYellowButton, characterFormat.BackgroundColor, "#FFF3A3");
        SetColorState(BackgroundBlueButton, characterFormat.BackgroundColor, "#BDE7FF");
        SetColorState(BackgroundGreenButton, characterFormat.BackgroundColor, "#D8F3C5");

        StatusLabel.Text = Editor.SelectedRange.IsEmpty
            ? $"Caret at {Editor.SelectedRange.Start}"
            : $"{Editor.SelectedRange.Length} characters selected";
    }

    private RichTextListMarker? GetSelectedListMarker(
        RichTextSelectionParagraphFormat paragraphFormat)
    {
        if (paragraphFormat.IsListMixed || paragraphFormat.List is not { } item ||
            !Editor.Document.CurrentSnapshot.Lists.TryGetValue(item.ListId, out var definition) ||
            (uint)item.Level >= (uint)definition.Levels.Length)
        {
            return null;
        }

        return definition.Levels[item.Level].Marker;
    }

    private static void SetToolbarState(Button button, bool active)
    {
        button.BackgroundColor = active ? ActiveToolbarColor : InactiveToolbarColor;
        button.TextColor = active ? Colors.White : InactiveToolbarTextColor;
    }

    private static Color? GetCommandColor(Button button) =>
        button.CommandParameter is string value && !string.IsNullOrWhiteSpace(value)
            ? Color.FromArgb(value)
            : null;

    private static void SetColorState(Button button, Color? selected, string? expected)
    {
        var target = expected is null ? null : Color.FromArgb(expected);
        button.BorderColor = ActiveToolbarColor;
        button.BorderWidth = ColorsEqual(selected, target) ? 3 : 0;
    }

    private static bool ColorsEqual(Color? left, Color? right)
    {
        if (left is null || right is null)
        {
            return left is null && right is null;
        }

        const float tolerance = 0.0001f;
        return Math.Abs(left.Red - right.Red) < tolerance &&
               Math.Abs(left.Green - right.Green) < tolerance &&
               Math.Abs(left.Blue - right.Blue) < tolerance &&
               Math.Abs(left.Alpha - right.Alpha) < tolerance;
    }
}
