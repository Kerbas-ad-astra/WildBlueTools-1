﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using KSP.IO;

/*
Source code copyright 2016, by Michael Billard (Angel-125)
License: GPLV3

Wild Blue Industries is trademarked by Michael Billard and may be used for non-commercial purposes. All other rights reserved.
Note that Wild Blue Industries is a ficticious entity 
created for entertainment purposes. It is in no way meant to represent a real entity.
Any similarity to a real entity is purely coincidental.

Portions of this software use code from the Firespitter plugin by Snjo, used with permission. Thanks Snjo for sharing how to switch meshes. :)

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
*/
namespace WildBlueIndustries
{
    public class LocalOpsManager : Window<LocalOpsManager>, IParentView
    {
        private Vector2 _scrollPosViews, _scrollPosButtons;
        Dictionary<string, List<SDrawbleView>> drawableViews = new Dictionary<string, List<SDrawbleView>>();
        List<SDrawbleView> views;
        string selectedButton = string.Empty;

        public LocalOpsManager() :
        base("Manage Operations", 950, 480)
        {
            Resizable = false;
        }

        #region IParentView
        public void SetParentVisible(bool isVisible)
        {
            SetVisible(isVisible);
        }
        #endregion

        public void OnModuleRedecorated(ConfigNode nodeTemplate)
        {
            reloadViews();
        }

        public override void SetVisible(bool newValue)
        {
            base.SetVisible(newValue);

            if (newValue)
                reloadViews();
        }

        protected void reloadViews()
        {
            List<IOpsView> opsViews;
            List<string> buttonLabels;
            SDrawbleView drawableView;
            Vessel vessel;
            IOpsView opsView;
            string label;
            int totalVessels, totalViews, totalLabels;

            drawableViews.Clear();

            //Find all the loaded vessels in physics range
            totalVessels = FlightGlobals.Vessels.Count;
            for (int vesselIndex = 0; vesselIndex < totalVessels; vesselIndex++)
            {
                vessel = FlightGlobals.Vessels[vesselIndex];
                if (vessel.mainBody != FlightGlobals.ActiveVessel.mainBody)
                    continue;

                if (vessel.loaded == false)
                    continue;

                //Now find all part modules in the vessel that implement IOpsViews
                opsViews = vessel.FindPartModulesImplementing<IOpsView>();
                totalViews = opsViews.Count;

                //Go through the list and get their drawable views
                for (int viewIndex = 0; viewIndex < totalViews; viewIndex++)
                {
                    opsView = opsViews[viewIndex];

                    //Set parent view
                    opsView.SetParentView(this);

                    if (opsView is WBIResourceSwitcher)
                    {
                        WBIResourceSwitcher switcher = (WBIResourceSwitcher)opsView;
                        switcher.onModuleRedecorated -= OnModuleRedecorated;
                        switcher.onModuleRedecorated += OnModuleRedecorated;
                    }

                    //Setup button labels
                    buttonLabels = opsView.GetButtonLabels();
                    totalLabels = buttonLabels.Count;
                    for (int labelIndex = 0; labelIndex < totalLabels; labelIndex++)
                    {
                        label = buttonLabels[labelIndex];
                        drawableView = new SDrawbleView();
                        drawableView.buttonLabel = label;
                        drawableView.view = opsView;
                        drawableView.vessel = vessel;
                        drawableView.partTitle = opsView.GetPartTitle();

                        if (drawableViews.ContainsKey(label) == false)
                            drawableViews.Add(label, new List<SDrawbleView>());

                        drawableViews[label].Add(drawableView);
                    }
                }

                if (drawableViews.Count > 0)
                {
                    selectedButton = drawableViews.Keys.First<string>();
                    views = drawableViews[selectedButton];
                }
            }
        }

        protected override void DrawWindowContents(int windowId)
        {
            GUILayout.BeginHorizontal();
            string[] viewKeys = drawableViews.Keys.ToArray<string>();
            int totalKeys = viewKeys.Length;
            string label;
            int totalViews;
            SDrawbleView drawableView;

            _scrollPosButtons = GUILayout.BeginScrollView(_scrollPosButtons);

            //Draw the buttons.
            for (int labelIndex = 0; labelIndex < totalKeys; labelIndex++)
            {
                label = viewKeys[labelIndex];
                if (GUILayout.Button(label))
                {
                    _scrollPosViews = new Vector2();
                    selectedButton = label;
                    views = drawableViews[label];
                }
            }
            GUILayout.EndScrollView();

            if (views == null)
            {
                GUILayout.Label("No views found!");
            }

            //Now draw the views
            else
            {
                _scrollPosViews = GUILayout.BeginScrollView(_scrollPosViews, new GUILayoutOption[] { GUILayout.Width(700) });
                totalViews = views.Count;
                for (int viewIndex = 0; viewIndex < totalViews; viewIndex++)
                {
                    drawableView = views[viewIndex];
                    GUILayout.BeginVertical();

                    GUILayout.BeginScrollView(new Vector2(0, 0), new GUIStyle(GUI.skin.textArea), GUILayout.Height(530));
                    GUILayout.Label(drawableView.vessel.vesselName + ": " + drawableView.partTitle);
                    drawableView.view.DrawOpsWindow(selectedButton);
                    GUILayout.EndScrollView();

                    GUILayout.EndVertical();
                }
                GUILayout.EndScrollView();
            }


            GUILayout.EndHorizontal();
        }
    }
}
