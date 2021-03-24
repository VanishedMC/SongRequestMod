
using System.Runtime.CompilerServices;
using IPA.Config.Stores;
using UnityEngine;

[assembly: InternalsVisibleTo(GeneratedStore.AssemblyVisibilityTarget)]
namespace SongRequest.Configuration
{
    internal class PluginConfig
    {
        public static PluginConfig Instance { get; set; }
        public virtual string PrivateKey { get; set; } = "ENTER KEY"; // Must be 'virtual' if you want BSIPA to detect a value change and save the config automatically.

        public virtual void OnReload()
        {
            if(PluginConfig.Instance != null)
            {
                Plugin.Log.Info(PluginConfig.Instance.PrivateKey);

                if(Plugin.requestButton != null)
                {
                    if (PluginConfig.Instance.PrivateKey.Equals("ENTER KEY"))
                    {
                        Plugin.requestButton.enabled = false;
                        UIHelper.AddHintText(Plugin.requestButton.transform as RectTransform, "Please set up your config first!");
                    }
                    else
                    {
                        Plugin.requestButton.enabled = true;
                        UIHelper.AddHintText(Plugin.requestButton.transform as RectTransform, "View song requests");
                    }
                }
            }
        }

        public virtual void Changed()
        {

        }

        public virtual void CopyFrom(PluginConfig other)
        {

        }
    }
}
