using Colossal;
using Colossal.IO.AssetDatabase;
using Game.Modding;
using Game.Settings;
using Game.UI;
using Game.UI.Widgets;
using System.Collections.Generic;

namespace ZoneDepth
{
    [FileLocation("SmartZoningExtension")]
    public class SmartZoningExtensionSettings : ModSetting
    {
        public const string kSection = "Main";

        public SmartZoningExtensionSettings(IMod mod) : base(mod)
        {

        }

        [SettingsUISection(kSection)]
        public bool Enabled { get; set; } = true;

        public override void SetDefaults()
        {
            Enabled = true;
        }
    }

    public class LocaleEN : IDictionarySource
    {
        private readonly SmartZoningExtensionSettings m_Setting;
        public LocaleEN(SmartZoningExtensionSettings setting)
        {
            m_Setting = setting;
        }
        public IEnumerable<KeyValuePair<string, string>> ReadEntries(IList<IDictionaryEntryError> errors, Dictionary<string, int> indexCounts)
        {
            return new Dictionary<string, string>
            {
                { m_Setting.GetSettingsLocaleID(), "Smart Zoning Extension" },
                { m_Setting.GetOptionTabLocaleID(SmartZoningExtensionSettings.kSection), "Main" },
                { m_Setting.GetOptionLabelLocaleID(nameof(SmartZoningExtensionSettings.Enabled)), "Enabled" },
                { m_Setting.GetOptionDescLocaleID(nameof(SmartZoningExtensionSettings.Enabled)), "Enable or disable all Smart Zoning Extension features." },
            };
        }

        public void Unload()
        {

        }
    }
}
