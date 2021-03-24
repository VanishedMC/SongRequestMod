using BeatSaberMarkupLanguage;
using HMUI;
using IPA.Utilities;
using System.Linq;
using UnityEngine;

namespace SongRequest
{
    public class RequestFlowCoordinator : FlowCoordinator
    {
        private RequestViewController viewController;
        
        public void Awake()
        {
            viewController = BeatSaberUI.CreateViewController<RequestViewController>();
        }

        protected override void DidActivate(bool firstActivation, bool addedToHierarchy, bool screenSystemEnabling)
        {
            if (firstActivation)
            {
                SetTitle("Song Requests");
                showBackButton = true;
                ProvideInitialViewControllers(viewController);
            }
        }

        protected override void BackButtonWasPressed(ViewController topViewController)
        {
            FlowCoordinator flowCoordinator;

            if (Plugin.gameMode == Plugin.GameMode.Solo)
            {
                flowCoordinator = Resources.FindObjectsOfTypeAll<SoloFreePlayFlowCoordinator>().First();
            }
            else
            {
                flowCoordinator = Resources.FindObjectsOfTypeAll<MultiplayerLevelSelectionFlowCoordinator>().First();
            }

            SetRightScreenViewController(null, ViewController.AnimationType.None);
            flowCoordinator.InvokeMethod<object, FlowCoordinator>("DismissFlowCoordinator", this, ViewController.AnimationDirection.Horizontal, null, false);
        }

        public void Dismiss()
        {
            BackButtonWasPressed(null);
        }

        public void SetTitle(string newTitle)
        {
            base.SetTitle(newTitle);
        }
    }
}