using System;
using System.Collections.Generic;
using System.Globalization;

namespace KekyNotepad.Services;

/// <summary>
/// Tiny UI translation layer. Code writes the English text; <see cref="T"/> returns the
/// Arabic text when Arabic is selected (and the English text when no translation exists).
/// </summary>
public static class Loc
{
    private static string _language = "en";

    public static event EventHandler? Changed;

    public static string Language
    {
        get => _language;
        set
        {
            var next = value == "ar" ? "ar" : "en";
            if (_language != next)
            {
                _language = next;
                Changed?.Invoke(null, EventArgs.Empty);
            }
        }
    }

    public static bool IsArabic => _language == "ar";

    /// <summary>Default for first run: Arabic when Windows is Arabic.</summary>
    public static string SystemDefault() =>
        CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ar" ? "ar" : "en";

    public static string T(string english) =>
        _language == "ar" && Arabic.TryGetValue(english, out var arabic) ? arabic : english;

    public static string F(string english, params object[] args) =>
        string.Format(CultureInfo.CurrentCulture, T(english), args);

    /// <summary>"Untitled 3" shown as "بدون عنوان 3" in Arabic; other names are untouched.</summary>
    public static string DisplayTitle(string title)
    {
        if (_language != "ar")
        {
            return title;
        }

        var match = System.Text.RegularExpressions.Regex.Match(title, @"^Untitled(?: (\d+))?$");
        if (!match.Success)
        {
            return title;
        }

        return T("Untitled") + (match.Groups[1].Success ? " " + match.Groups[1].Value : string.Empty);
    }

    private static readonly Dictionary<string, string> Arabic = new()
    {
        // Settings page
        ["Keky Notepad"] = "كي كي",
        ["Untitled"] = "بدون عنوان",
        ["Language of menus, tooltips and dialogs."] = "لغة القوائم والتلميحات والنوافذ.",
        ["Color theme of the whole app."] = "نمط ألوان البرنامج كله.",
        ["Black"] = "أسود",
        ["Send files to the phone (APKs are installed)"] = "إرسال ملفات إلى الهاتف (ملفات APK تُثبَّت)",
        ["Record the phone screen"] = "تسجيل فيديو لشاشة الهاتف",
        ["Stop recording"] = "إيقاف التسجيل",
        ["Could not start recording."] = "تعذّر بدء التسجيل.",
        ["Recording saved:\n{0}"] = "حُفظ التسجيل:\n{0}",
        ["Could not save the recording.\n{0}"] = "تعذّر حفظ التسجيل.\n{0}",
        ["Sent {0} file(s) to the phone."] = "تم إرسال {0} ملف إلى الهاتف.",
        ["Sent {0} file(s). Failed:\n{1}"] = "أُرسل {0} ملف. فشل:\n{1}",
        ["Clear screen"] = "مسح الشاشة",
        ["Find and replace"] = "بحث واستبدال",
        ["Replace:"] = "استبدال:",
        ["Replace"] = "استبدال",
        ["Replace all"] = "استبدال الكل",
        ["Previous"] = "السابق",
        ["All tabs"] = "كل التبويبات",
        ["Match case"] = "مطابقة حالة الأحرف",
        ["Search in all open tabs"] = "البحث في كل التبويبات المفتوحة",
        ["Previous match (Shift+F3)"] = "النتيجة السابقة (Shift+F3)",
        ["Next match (F3)"] = "النتيجة التالية (F3)",
        ["Wrapped around"] = "عاد البحث من البداية",
        ["No results"] = "لا توجد نتائج",
        ["{0} replaced"] = "تم استبدال {0}",

        ["Auto-save files"] = "حفظ تلقائي للملفات",
        ["Saves a changed file about a second after you stop typing."] = "يحفظ الملف بعد ثانية تقريبًا من توقفك عن الكتابة.",
        ["Solarized"] = "سولارايزد",
        ["Also Ctrl++ and Ctrl+- anywhere in the editor."] = "ويمكن أيضًا بـ Ctrl++ و Ctrl+- داخل المحرر.",
        ["Automatic follows the text, or force one direction for every tab."] = "التلقائي يتبع النص، أو ثبّت اتجاهًا واحدًا لكل التبويبات.",
        ["Wrap long lines inside the current tab."] = "التفاف الأسطر الطويلة في التبويب الحالي.",
        ["Show a numbered column next to the text."] = "إظهار عمود ترقيم بجانب النص.",
        ["Highlighting used for the current tab."] = "نوع التلوين المستخدم في التبويب الحالي.",
        ["Shell used by the terminal in the Device tab."] = "واجهة الأوامر التي يستخدمها التيرمنل في تبويب الجهاز.",
        ["Open"] = "فتح",
        ["Text direction"] = "اتجاه النص",
        ["Theme"] = "المظهر",
        ["Device and terminal"] = "الجهاز والتيرمنل",
        ["Save As"] = "حفظ باسم",
        ["Find"] = "بحث",
        ["Default text size"] = "الحجم الافتراضي للنص",
        ["Settings"] = "الإعدادات",
        ["General"] = "عام",
        ["Interface language"] = "لغة البرنامج",
        ["Theme"] = "المظهر",
        ["Light"] = "فاتح",
        ["Dark"] = "داكن",
        ["Editor"] = "المحرر",
        ["Font"] = "الخط",
        ["Change..."] = "تغيير...",
        ["Font size"] = "حجم الخط",
        ["Text direction"] = "اتجاه النص",
        ["Auto"] = "تلقائي",
        ["Left to right"] = "يسار ← يمين",
        ["Right to left"] = "يمين ← يسار",
        ["Word wrap"] = "التفاف الأسطر",
        ["Line numbers"] = "ترقيم الأسطر",
        ["Syntax language (this tab)"] = "لغة التلوين (هذا التبويب)",
        ["On"] = "تشغيل",
        ["Off"] = "إيقاف",
        ["Device and terminal"] = "الجهاز والتيرمنل",
        ["Device panel"] = "لوحة الجهاز",
        ["Terminal shell"] = "واجهة التيرمنل",
        ["scrcpy location"] = "مسار scrcpy",
        ["Browse..."] = "استعراض...",
        ["Not set (found automatically)"] = "غير محدد (يُكتشف تلقائيًا)",
        ["Files"] = "الملفات",
        ["New tab"] = "تبويب جديد",
        ["Open..."] = "فتح...",
        ["Save"] = "حفظ",
        ["Save As..."] = "حفظ باسم...",
        ["Close tab"] = "إغلاق التبويب",
        ["Find..."] = "بحث...",
        ["Keyboard shortcuts"] = "اختصارات لوحة المفاتيح",
        ["About"] = "حول البرنامج",
        ["A code editor, a notepad and a phone screen mirror in one app, connected through a single cable."] = "محرر أكواد ومفكرة ومشغّل هواتف عبر الشاشة، في تطبيق واحد يرتبط بالهاتف مباشرةً بوصلة واحدة.",
        ["Version"] = "الإصدار",
        ["Don't save"] = "عدم الحفظ",
        ["Your changes will be lost if you don't save them."] = "ستفقد تغييراتك إن لم تحفظها.",
        ["Get the most out of your time and be more productive."] = "استفد أقصى استفادة وحقق إنتاجية أكبر.",
        ["Language"] = "اللغة",
        ["Reset"] = "افتراضي",

        // Shortcuts text
        ["Ctrl+N  New tab"] = "Ctrl+N  تبويب جديد",
        ["Ctrl+,  Settings"] = "Ctrl+,  الإعدادات",
        ["Ctrl+O  Open"] = "Ctrl+O  فتح",
        ["Ctrl+S  Save"] = "Ctrl+S  حفظ",
        ["Ctrl+Shift+S  Save As"] = "Ctrl+Shift+S  حفظ باسم",
        ["Ctrl+W  Close tab"] = "Ctrl+W  إغلاق التبويب",
        ["Ctrl+F  Find"] = "Ctrl+F  بحث",
        ["Ctrl+R  Text direction: auto / LTR / RTL"] = "Ctrl+R  اتجاه النص: تلقائي / يسار / يمين",
        ["Ctrl++ / Ctrl+-  Larger / smaller text"] = "Ctrl++ / Ctrl+-  تكبير / تصغير النص",
        ["Ctrl+0  Default text size"] = "Ctrl+0  الحجم الافتراضي للنص",
        ["Ctrl+D  Light / dark theme"] = "Ctrl+D  مظهر فاتح / داكن",
        ["Ctrl+`  Device and terminal"] = "Ctrl+`  الجهاز والتيرمنل",
        ["Double-click tab  Rename"] = "نقر مزدوج على التبويب  إعادة تسمية",
        ["Right-click  Copy / paste"] = "نقر يمين  نسخ / لصق",

        // Find bar
        ["Find:"] = "بحث:",
        ["Next"] = "التالي",
        ["Close"] = "إغلاق",

        // Dialogs
        ["Open file"] = "فتح ملف",
        ["Save changes to \"{0}\"?"] = "هل تريد حفظ التغييرات في \"{0}\"؟",
        ["Could not open the file:\n{0}\n\n{1}"] = "تعذّر فتح الملف:\n{0}\n\n{1}",
        ["Could not save the file:\n{0}\n\n{1}"] = "تعذّر حفظ الملف:\n{0}\n\n{1}",
        ["Yes"] = "نعم",
        ["No"] = "لا",
        ["Cancel"] = "إلغاء",
        ["OK"] = "موافق",
        ["Size"] = "الحجم",
        ["Preview"] = "معاينة",

        // Title bar tooltips
        ["Undo  (Ctrl+Z)"] = "تراجع  (Ctrl+Z)",
        ["Redo  (Ctrl+Y)"] = "إعادة  (Ctrl+Y)",
        ["Save  (Ctrl+S)"] = "حفظ  (Ctrl+S)",
        ["Find  (Ctrl+F)"] = "بحث  (Ctrl+F)",
        ["Smaller text  (Ctrl+-)"] = "تصغير النص  (Ctrl+-)",
        ["Larger text  (Ctrl++)"] = "تكبير النص  (Ctrl++)",
        ["Light / dark theme  (Ctrl+D)"] = "مظهر فاتح / داكن  (Ctrl+D)",
        ["Editor font  (Ctrl++ / Ctrl+- / Choose font...)"] = "خط المحرر  (اختيار الخط...)",
        ["Text direction: Auto / Left-to-right / Right-to-left  (Ctrl+R)"] = "اتجاه النص: تلقائي / يسار-يمين / يمين-يسار  (Ctrl+R)",
        ["New tab  (Ctrl+N)"] = "تبويب جديد  (Ctrl+N)",
        ["Device"] = "الجهاز",
        ["Settings  (menu)"] = "الإعدادات",

        // Device
        ["Looking for a phone...\nConnect it with USB and enable USB debugging."] = "جارٍ البحث عن هاتف...\nوصّله بكابل USB وفعّل تصحيح USB.",
        ["scrcpy.exe was not found.\nReopen this tab to choose its location."] = "لم يُعثر على scrcpy.exe.\nأعد فتح هذا التبويب لتحديد مكانه.",
        ["Locate scrcpy.exe"] = "حدد مكان scrcpy.exe",
        ["Start"] = "تشغيل",
        ["Stop"] = "إيقاف",
        ["Device stopped"] = "الجهاز متوقف",
        ["Connecting to the device..."] = "جارٍ الاتصال بالجهاز...",
        ["Connected"] = "متصل",
        ["Installing..."] = "جارٍ التثبيت...",
        ["Screenshot (saved + copied)"] = "لقطة شاشة (تُحفظ وتُنسخ)",
        ["Rotate screen"] = "تدوير الشاشة",
        ["Back"] = "رجوع",
        ["Home"] = "الرئيسية",
        ["Recent apps"] = "التطبيقات الأخيرة",
        ["Turn the phone's own screen off while mirroring"] = "إطفاء شاشة الهاتف نفسه أثناء العرض",
        ["Switch to Wi-Fi (then unplug the cable)"] = "التحويل إلى Wi-Fi (ثم افصل الكابل)",
        ["Show logcat in the terminal"] = "عرض سجل logcat في التيرمنل",
        ["Install an APK"] = "تثبيت ملف APK",
        ["Could not take a screenshot."] = "تعذّر أخذ لقطة الشاشة.",
        ["Screenshot saved and copied:\n{0}"] = "حُفظت اللقطة ونُسخت:\n{0}",
        ["Could not find the phone's Wi-Fi address. Is Wi-Fi connected on the phone?"] = "تعذّر إيجاد عنوان Wi-Fi للهاتف. هل Wi-Fi متصل في الهاتف؟",
        ["adb tcpip failed:\n{0}"] = "فشل adb tcpip:\n{0}",
        ["{0}\n\nYou can unplug the cable now. The Wi-Fi device appears as a new view."] = "{0}\n\nيمكنك فصل الكابل الآن. سيظهر جهاز Wi-Fi كعرض جديد.",
        ["adb connect failed:\n{0}"] = "فشل adb connect:\n{0}",
        ["APK installed."] = "تم تثبيت التطبيق.",
        ["Install failed:\n{0}"] = "فشل التثبيت:\n{0}",
        ["Install APK"] = "تثبيت APK",
        ["Android package"] = "حزمة أندرويد",
    };
}
