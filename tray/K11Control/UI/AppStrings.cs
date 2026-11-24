using K11Control.Config;

namespace K11Control.UI;

internal static class AppStrings
{
    public static string Tr(AppLanguage lang, string ru, string en) => lang == AppLanguage.En ? en : ru;

    public static string NavOutput(AppLanguage l) => Tr(l, "Выход и режимы", "Output & Modes");
    public static string NavLcd(AppLanguage l) => Tr(l, "Экран LCD", "LCD Display");
    public static string NavRgb(AppLanguage l) => Tr(l, "Подсветка RGB", "RGB Lighting");
    public static string NavHotkeys(AppLanguage l) => Tr(l, "Горячие клавиши", "Hotkeys");
    public static string NavDiag(AppLanguage l) => Tr(l, "Диагностика", "Diagnostics");

    // Top page header matches the active left sidebar label 1:1
    public static string PageTitle(AppLanguage l, int idx) => idx switch
    {
        0 => NavOutput(l),
        1 => NavLcd(l),
        2 => NavRgb(l),
        3 => NavHotkeys(l),
        _ => NavDiag(l)
    };

    public static string StatusAudioMuted(AppLanguage l) => Tr(l, "Звук выключен", "Audio Muted");
    public static string StatusAudioActive(AppLanguage l) => Tr(l, "Звук включён", "Audio Active");
    public static string StatusAudioUnknown(AppLanguage l) => Tr(l, "ЦАП отключён", "DAC Offline");
    public static string BtnClose(AppLanguage l) => Tr(l, "Закрыть", "Close");
    public static string BtnApply(AppLanguage l) => Tr(l, "Применить", "Apply");
    public static string BtnApplyDone(AppLanguage l) => Tr(l, "Применено ✓", "Applied ✓");
    public static string BtnApplyOffline(AppLanguage l) => Tr(l, "Нет связи", "Offline");
    public static string BtnApplyError(AppLanguage l) => Tr(l, "Ошибка", "Error");
    public static string BtnSaveClose(AppLanguage l) => Tr(l, "Сохранить и закрыть", "Save & Close");

    public static string OutBalanceWarn(AppLanguage l, float ch0Db, float ch1Db) =>
        Tr(l,
            $"Баланс: {FormatDbShort(ch0Db)} / {FormatDbShort(ch1Db)}",
            $"Balance: {FormatDbShort(ch0Db)} / {FormatDbShort(ch1Db)}");

    public static string SidebarBalanceTarget(AppLanguage l, float targetDb) =>
        Tr(l, $"Выровнять → {FormatDbShort(targetDb)} дБ", $"Fix L/R → {FormatDbShort(targetDb)} dB");

    public static string OutBalanceFixBtn(AppLanguage l) => Tr(l, "Выровнять", "Balance");

    private static string FormatDbShort(float db)
    {
        float clean = MathF.Abs(db) < 0.05f ? 0f : MathF.Round(db, 1);
        return clean.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);
    }

    // Output Page (uniform naming across buttons, tray menu, and hotkeys)
    public static string OutCardTitle(AppLanguage l) => Tr(l, "Переключение выхода ЦАПа (LO отключён)", "DAC Output Switching (LO Disabled)");
    public static string OutToggleBtn(AppLanguage l) => Tr(l, "Переключить выход", "Toggle Output");
    public static string OutPreBtn(AppLanguage l) => Tr(l, "Колонки (LO SE)", "Speakers (LO SE)");
    public static string OutPoBtn(AppLanguage l) => Tr(l, "Наушники (PO)", "Headphones (PO)");

    public static string DacModesCardTitle(AppLanguage l) => Tr(l, "Усиление (G) и фильтр ЦАПа (OS / NOS)", "DAC Gain (G) & Sampling Filter (OS / NOS)");
    public static string GainRowLabel(AppLanguage l) => Tr(l, "Усиление (G):", "Gain (G):");
    public static string GainLowBtn(AppLanguage l) => Tr(l, "L • Низкое", "L • Low");
    public static string GainMidBtn(AppLanguage l) => Tr(l, "M • Среднее", "M • Mid");
    public static string GainHighBtn(AppLanguage l) => Tr(l, "H • Высокое", "H • High");
    public static string FilterRowLabel(AppLanguage l) => Tr(l, "Режим ЦАПа:", "DAC Filter:");
    public static string FilterOsBtn(AppLanguage l) => Tr(l, "OS • Оверсэмплинг", "OS • Oversampling");
    public static string FilterNosBtn(AppLanguage l) => Tr(l, "NOS • Прямой R2R", "NOS • Direct R2R");

    public static string StartupCardTitle(AppLanguage l) => Tr(l, "Запуск и автостарт", "Startup & Autostart");
    public static string AutostartSwitchText(AppLanguage l) => Tr(
        l,
        "Запускать K11 Control вместе с Windows",
        "Launch K11 Control with Windows");
    public static string StartupSwitchText(AppLanguage l) => Tr(
        l,
        "При запуске программы всегда включать:",
        "On startup, always switch to:");
    public static string StartupPo(AppLanguage l) => Tr(l, "Наушники (PO)", "Headphones (PO)");
    public static string StartupPre(AppLanguage l) => Tr(l, "Колонки (LO SE)", "Speakers (LO SE)");

    public static string RebootBtn(AppLanguage l) => Tr(l, "Перезагрузить ЦАП", "Reboot DAC");
    public static string UsbHint(AppLanguage l) => Tr(
        l,
        "Программный сброс (NVIC_SystemReset).\nДля Ryzen / B450 лучше порты CPU (Hub_#0002).",
        "Software reset (NVIC_SystemReset), no replug.\nOn Ryzen / B450, prefer CPU ports (Hub_#0002).");

    // LCD Page
    public static string LcdCardTitle(AppLanguage l) => Tr(l, "Режим экрана ЦАПа", "DAC Screen Mode");
    public static string LcdNormal(AppLanguage l) => Tr(l, "Обычный", "Normal");
    public static string LcdTextMode(AppLanguage l) => Tr(l, "Текст", "Text");
    public static string LcdSegmentsMode(AppLanguage l) => Tr(l, "Палочки", "Segments");
    public static string LcdSettingsTitle(AppLanguage l) => Tr(l, "Параметры экрана", "Screen Settings");
    public static string LcdTextLbl(AppLanguage l) => Tr(l, "Текст (до 7 символов):", "Text (up to 7 chars):");
    public static string LcdPresetLbl(AppLanguage l) => Tr(l, "Пресет:", "Preset:");
    public static string LcdSavePresetBtn(AppLanguage l) => Tr(l, "Сохранить", "Save");
    public static string LcdSavePresetDone(AppLanguage l) => Tr(l, "Сохранено ✓", "Saved ✓");
    public static string LcdMenuDuplicate(AppLanguage l, bool fromBuiltIn) => fromBuiltIn
        ? Tr(l, "Дублировать в свой...", "Duplicate to Custom...")
        : Tr(l, "Дублировать пресет...", "Duplicate Preset...");
    public static string LcdMenuNewPreset(AppLanguage l) => Tr(l, "+ Новый пустой пресет...", "+ New Blank Preset...");
    public static string LcdMenuTemplate(AppLanguage l) => Tr(l, "Применить шаблон", "Apply Template");
    public static string LcdMenuResetPreset(AppLanguage l) => Tr(l, "Сбросить по умолчанию", "Reset to Default");
    public static string LcdMenuRename(AppLanguage l) => Tr(l, "Переименовать...", "Rename...");
    public static string LcdMenuDelete(AppLanguage l) => Tr(l, "Удалить пресет", "Delete Preset");
    public static string LcdNewPresetTitle(AppLanguage l) => Tr(l, "Новый пресет палочек", "New Segment Preset");
    public static string LcdDuplicatePresetTitle(AppLanguage l) => Tr(l, "Сохранить копию пресета", "Save Preset Copy");
    public static string LcdRenamePresetTitle(AppLanguage l) => Tr(l, "Переименовать пресет", "Rename Preset");
    public static string LcdNewPresetPrompt(AppLanguage l) => Tr(l, "Название пресета:", "Preset name:");
    public static string SticksStarterCustomName(AppLanguage l, int idx) => Tr(l, $"Мой пресет {idx}", $"My Preset {idx}");
    public static string LcdStockLockedHint(AppLanguage l) => Tr(
        l,
        "Встроенный пресет защищён: менять можно текст и скорость, а для рисования выберите «••• → Дублировать в свой...»",
        "Built-in preset segments are locked: edit text or speed directly, or use ••• → Duplicate to Custom to draw");
    public static string LcdDuplicateDefaultName(AppLanguage l, string sourceName)
    {
        string suffix = Tr(l, " — копия", " Copy");
        string baseName = string.IsNullOrWhiteSpace(sourceName) ? Tr(l, "Пресет", "Preset") : sourceName.Trim();
        int maxBase = Math.Max(1, 24 - suffix.Length);
        if (baseName.Length > maxBase)
            baseName = baseName[..maxBase].TrimEnd();
        return baseName + suffix;
    }
    public static string LcdTemplateName(AppLanguage l, SticksTemplateKind k) => k switch
    {
        SticksTemplateKind.ReelsText => Tr(l, "Бобинник + текст", "Reels + Text"),
        SticksTemplateKind.BracketsText => Tr(l, "Скобки + текст", "Brackets + Text"),
        SticksTemplateKind.Impulse => Tr(l, "Импульс", "Impulse"),
        SticksTemplateKind.PulseEkg => Tr(l, "Пульс (ЭКГ)", "Pulse (EKG)"),
        SticksTemplateKind.Orbit => Tr(l, "Орбита", "Orbit"),
        SticksTemplateKind.Rain => Tr(l, "Дождь", "Rain"),
        SticksTemplateKind.SingingFace => Tr(l, "Поёт (^O^)", "Singing (^O^)"),
        _ => Tr(l, "Пустой (1 кадр)", "Blank (1 frame)")
    };
    public static string SticksStarterPresetName(AppLanguage l, SticksTemplateKind k) => k switch
    {
        SticksTemplateKind.ReelsText => Tr(l, "Бобинник (R2R)", "Reels (R2R)"),
        SticksTemplateKind.BracketsText => Tr(l, "Скобки (HI-FI)", "Brackets (HI-FI)"),
        SticksTemplateKind.Impulse => Tr(l, "Импульс", "Impulse"),
        SticksTemplateKind.PulseEkg => Tr(l, "Пульс (ЭКГ)", "Pulse (EKG)"),
        SticksTemplateKind.Orbit => Tr(l, "Орбита", "Orbit"),
        SticksTemplateKind.Rain => Tr(l, "Дождь", "Rain"),
        SticksTemplateKind.SingingFace => Tr(l, "Поёт (^O^)", "Singing (^O^)"),
        _ => Tr(l, "Пресет", "Preset")
    };
    public static string LcdWordAndSpeedLbl(AppLanguage l) => Tr(l, "Текст / шаг:", "Text / step:");
    public static string LcdSpeedOnlyLbl(AppLanguage l) => Tr(l, "Шаг анимации:", "Animation step:");
    public static string LcdFramePrefix(AppLanguage l) => Tr(l, "Кадр", "Frame");
    public static string LcdFrameCountShort(AppLanguage l, int n) => l == AppLanguage.En
        ? (n == 1 ? "1 frame" : $"{n} frames")
        : n switch { 1 => "1 кадр", 2 => "2 кадра", 3 => "3 кадра", _ => "4 кадра" };
    public static string LcdFrameCountMenu(AppLanguage l, int n) => l == AppLanguage.En
        ? (n == 1 ? "1 frame (static)" : $"{n} frames")
        : n switch { 1 => "1 кадр (статика)", 2 => "2 кадра", 3 => "3 кадра", _ => "4 кадра" };
    public static string LcdClearSticksBtn(AppLanguage l) => Tr(l, "Очистить", "Clear");
    public static string LcdDelayLbl(AppLanguage l) => Tr(l, "Задержка возврата:", "Revert delay:");
    public static string LcdScreenOff(AppLanguage l) => Tr(
        l,
        "Выключить экран (отключение дисплея и подсветки в простое)",
        "Turn off display (disable screen & backlight at idle)");
    public static string LcdHideIcons(AppLanguage l) => Tr(
        l,
        "Скрывать верхние иконки (G / PCM / PO / LO SE)",
        "Hide upper glass icons (G / PCM / PO / LO SE)");

    // RGB Studio (clean labels without parentheses)
    public static string RgbModeTitle(AppLanguage l) => Tr(l, "Режим подсветки", "Lighting Mode");
    public static string RgbOff(AppLanguage l) => Tr(l, "Выкл", "Off");
    public static string RgbOn(AppLanguage l) => Tr(l, "Вкл", "On");
    public static string RgbCustom(AppLanguage l) => Tr(l, "Кастом", "Custom");
    public static string RgbEffectName(AppLanguage l, RgbEffectMode ef) => ef switch
    {
        RgbEffectMode.Solid => Tr(l, "Статика", "Solid"),
        RgbEffectMode.Breathing => Tr(l, "Дыхание", "Breathing"),
        RgbEffectMode.Rainbow => Tr(l, "Радуга", "Rainbow"),
        _ => Tr(l, "Волна", "Chase")
    };
    public static string RgbZoneAll(AppLanguage l) => Tr(l, "Все 3 зоны", "All 3 Zones");
    public static string RgbZone1(AppLanguage l) => Tr(l, "Зона 1 (Лев.)", "Zone 1 (Left)");
    public static string RgbZone2(AppLanguage l) => Tr(l, "Зона 2 (Центр)", "Zone 2 (Center)");
    public static string RgbZone3(AppLanguage l) => Tr(l, "Зона 3 (Прав.)", "Zone 3 (Right)");
    public static string RgbBrightLbl(AppLanguage l) => Tr(l, "Яркость:", "Brightness:");
    public static string RgbPresetsLbl(AppLanguage l) => Tr(l, "Быстрые цвета и HEX:", "Quick Swatches & HEX:");
    public static string RgbSyncAllBtn(AppLanguage l) => Tr(l, "Применить цвет ко всем 3 зонам", "Apply Color to All 3 Zones");

    // Hotkeys Page
    public static string HkSub(AppLanguage l) => Tr(l, "Кликните по полю и нажмите сочетание клавиш. Esc / Backspace / × — очистить.", "Click a field and press keys. Esc / Backspace / × clears.");
    public static string HkRowToggle(AppLanguage l) => Tr(l, "Переключить выход:", "Toggle Output:");
    public static string HkRowPre(AppLanguage l) => Tr(l, "Колонки (LO SE):", "Speakers (LO SE):");
    public static string HkRowPo(AppLanguage l) => Tr(l, "Наушники (PO):", "Headphones (PO):");
    public static string HkRowLcd(AppLanguage l) => Tr(l, "Экран (Обычный / Кастом):", "LCD (Normal / Custom):");
    public static string HkRowRgb(AppLanguage l) => Tr(l, "Подсветка RGB (Вкл / Выкл / Кастом):", "RGB (On / Off / Custom):");
    public static string HkRowMute(AppLanguage l) => Tr(l, "Включить / выключить звук:", "Mute / Unmute Sound:");
    public static string HkRowGainCycle(AppLanguage l) => Tr(l, "Переключить усиление (L → M → H):", "Cycle Gain (L → M → H):");
    public static string HkRowFilterToggle(AppLanguage l) => Tr(l, "Переключить фильтр (OS / NOS):", "Toggle DAC Filter (OS / NOS):");
    public static string HkRowReboot(AppLanguage l) => Tr(l, "Перезагрузить ЦАП (Reboot DAC):", "Reboot DAC (NVIC_SystemReset):");
    public static string HkResetDefaults(AppLanguage l) => Tr(l, "Сбросить по умолчанию", "Reset Defaults");

    // Diagnostics Page
    public static string DiagRefresh(AppLanguage l) => Tr(l, "Обновить статус", "Refresh Status");
    public static string DiagCh0(AppLanguage l) => Tr(l, "Кан. Л -1/0 дБ", "Ch0 -1/0 dB");
    public static string DiagCh1(AppLanguage l) => Tr(l, "Кан. П -1/0 дБ", "Ch1 -1/0 dB");
    public static string DiagMute(AppLanguage l) => Tr(l, "Перекл. Mute", "Mute Toggle");
    public static string DiagCol01(AppLanguage l) => Tr(l, "Тест COL01", "Probe COL01");
    public static string DiagCol02(AppLanguage l) => Tr(l, "Тест COL02", "Probe COL02");
    public static string DiagFlasher(AppLanguage l) => Tr(l, "Открыть программатор", "Open Flasher");
    public static string DiagNoEndpoint(AppLanguage l, uint? ch) => ch is null
        ? Tr(l, "Аудиовыход K11 R2R не найден.", "K11 R2R audio endpoint not found.")
        : Tr(l, $"Аудиовыход K11 R2R не найден (Ch{ch}).", $"K11 R2R audio endpoint not found (Ch{ch}).");
    public static string DiagSentChDb(AppLanguage l, uint ch, float db, bool ok) => ok
        ? Tr(l, $"UAC2 Ch{ch} установлен на {db:0.#} дБ (LCD-коды показываются только на прошивке stage0-diag).", $"UAC2 Ch{ch} set to {db:0.#} dB (LCD hex popups require stage0-diag firmware).")
        : Tr(l, $"Не удалось задать уровень Ch{ch}.", $"Failed to set Ch{ch}.");
    public static string DiagSentMute(AppLanguage l, bool mute, bool ok) => ok
        ? Tr(l, $"UAC2 Mute установлен: {(mute ? "Вкл" : "Выкл")} (прошивка M79 не выводит UAC Mute на LCD).", $"UAC2 Mute set to {mute} (M79 firmware does not show UAC Mute on LCD).")
        : Tr(l, "Не удалось переключить Mute.", "Failed to toggle Mute.");
    public static string DiagFlasherMissing(AppLanguage l, string path) => Tr(
        l,
        $"Программатор не найден рядом с K11Control.exe ({path}).",
        $"Programmer not found next to K11Control.exe ({path}).");
    public static string DiagFlasherLaunched(AppLanguage l) => Tr(
        l,
        "Запущен программатор GD32_MCU_HID_IAP_Programmer.exe",
        "Launched GD32_MCU_HID_IAP_Programmer.exe");

    // Tray menu
    public static string TrayStatus(AppLanguage l, bool online) => online
        ? Tr(l, "FiiO K11 R2R  •  Подключён", "FiiO K11 R2R  •  Connected")
        : Tr(l, "FiiO K11 R2R  •  Отключён", "FiiO K11 R2R  •  Disconnected");
    public static string TrayBalanceFix(AppLanguage l) => Tr(l,
        "Выровнять баланс L/R",
        "Fix L/R balance");
    public static string TrayTooltip(AppLanguage l, bool online, bool muted = false, bool imbalanced = false)
    {
        if (!online)
            return Tr(l, "FiiO K11 R2R Control — Отключён", "FiiO K11 R2R Control — Offline");
        if (imbalanced)
        {
            return muted
                ? Tr(l, "FiiO K11 R2R — Звук выкл. • Баланс L/R", "FiiO K11 R2R — Muted • L/R skewed")
                : Tr(l, "FiiO K11 R2R — Баланс L/R сбит!", "FiiO K11 R2R — L/R balance off!");
        }
        return muted
            ? Tr(l, "FiiO K11 R2R Control — Подключён • Звук выкл.", "FiiO K11 R2R Control — Online • Muted")
            : Tr(l, "FiiO K11 R2R Control — Подключён", "FiiO K11 R2R Control — Online");
    }
    public static string TrayMute(AppLanguage l, bool muted) => muted
        ? Tr(l, "Звук выключен", "Audio Muted")
        : Tr(l, "Выключить звук", "Mute Audio");
    public static string TrayLcdNormal(AppLanguage l) => Tr(l, "Экран: Обычный", "LCD: Normal");
    public static string TrayLcdText(AppLanguage l) => Tr(l, "Экран: Текст", "LCD: Text");
    public static string TrayLcdSegments(AppLanguage l) => Tr(l, "Экран: Палочки", "LCD: Segments");
    public static string TrayRgbOff(AppLanguage l) => Tr(l, "Подсветка: Выкл", "RGB: Off");
    public static string TrayRgbOn(AppLanguage l) => Tr(l, "Подсветка: Вкл", "RGB: On");
    public static string TrayRgbCustom(AppLanguage l) => Tr(l, "Подсветка: Кастом", "RGB: Custom");
    public static string TrayGainMenu(AppLanguage l) => Tr(l, "Усиление G", "Gain G");
    public static string TrayGainCycle(AppLanguage l) => Tr(l, "Следующее усиление", "Cycle Gain");
    public static string TrayFilterMenu(AppLanguage l) => Tr(l, "Фильтр ЦАПа", "DAC Filter");
    public static string TrayFilterToggle(AppLanguage l) => Tr(l, "Переключить", "Toggle");
    public static string TrayOpenApp(AppLanguage l) => Tr(l, "Открыть K11 Control...", "Open K11 Control...");
    public static string TrayExit(AppLanguage l) => Tr(l, "Выход", "Exit");
}
