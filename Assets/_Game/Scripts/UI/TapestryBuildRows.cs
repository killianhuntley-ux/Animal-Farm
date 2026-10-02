using AnimalFarm.Spirits;
using UnityEngine;
using UnityEngine.Events;

namespace AnimalFarm.UI
{
    /// <summary>
    /// Build-menu rows for carried tapestry banners (muscle 06): one "Hang"
    /// row per banner in the pouch. Picking a row closes the menu and enters
    /// the ghost placement mode (WeaveArchive.BeginHang) - the banner leaves
    /// the pouch only when it is actually hung. Kept out of HomePickerUI so
    /// that file only needs two one-line hooks.
    /// </summary>
    public static class TapestryBuildRows
    {
        /// <summary>True when at least one banner is in the pouch.</summary>
        public static bool AnyCarried
        {
            get
            {
                var archive = WeaveArchive.Instance;
                return archive != null && archive.CarriedBanners().Count > 0;
            }
        }

        /// <summary>Appends the divider, header and one row per carried banner (nothing if none).</summary>
        public static void Build(RectTransform panel, UnityAction close)
        {
            var archive = WeaveArchive.Instance;
            if (archive == null || panel == null) return;

            var carried = archive.CarriedBanners();
            if (carried.Count == 0) return;

            UIStyle.MakeDivider(panel);
            var header = UIRoot.MakeText(panel, "TapestriesHeader", 20,
                TextAnchor.MiddleCenter, UIStyle.Grey);
            header.text = "Tapestries (carried)";
            header.rectTransform.sizeDelta = new Vector2(0f, 26f);

            for (int i = 0; i < carried.Count; i++)
            {
                var record = carried[i]; // capture for the click closure
                string label = "Hang: " + record.parentAName + " & " + record.parentBName;
                var b = UIStyle.MakeButton(panel, label, () =>
                {
                    close?.Invoke();
                    archive.BeginHang(record);
                }, 20);
                ((RectTransform)b.transform).sizeDelta = new Vector2(0f, 40f);
            }
        }
    }
}
