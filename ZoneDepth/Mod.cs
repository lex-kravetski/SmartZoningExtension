using Colossal.Logging;
using Game;
using Game.Modding;
using Game.SceneFlow;
using Colossal.IO.AssetDatabase;

namespace ZoneDepth
{
    public class Mod : IMod
    {
        public static ILog log = LogManager.GetLogger($"{nameof(ZoneDepth)}.{nameof(Mod)}").SetShowsErrorsInUI(false);
        public static Setting CurrentSetting { get; private set; }
        private Setting m_Setting;

        public void OnLoad(UpdateSystem updateSystem)
        {
            m_Setting = new Setting(this);
            CurrentSetting = m_Setting;
            m_Setting.RegisterInOptionsUI();
            GameManager.instance.localizationManager.AddSource("en-US", new LocaleEN(m_Setting));


            AssetDatabase.global.LoadSettings(nameof(ZoneDepth), m_Setting, new Setting(this));

            // The experimental block-replacement pipeline is intentionally not
            // registered while the road-side toggle is being tested.  Recreating
            // zone blocks after every paint caused the vanilla zoning pipeline to
            // rebuild additional blocks when construction started.
            updateSystem.UpdateAt<ZoneSideInZoningSystem>(SystemUpdatePhase.ToolUpdate);
            // Apply the cell highlight after all modification systems have
            // finished, immediately before culling/rendering.
            updateSystem.UpdateAt<ZoneSideHighlightApplySystem>(SystemUpdatePhase.PreCulling);
        }

        public void OnDispose()
        {
            if (m_Setting != null)
            {
                m_Setting.ApplyAndSave();
                m_Setting.UnregisterInOptionsUI();
                m_Setting = null;
                CurrentSetting = null;
            }
        }
    }
}
