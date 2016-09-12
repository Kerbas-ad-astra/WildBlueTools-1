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

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
*/
namespace WildBlueIndustries
{
    [KSPModule("Resource Converter")]
    public class WBIResourceConverter : ModuleResourceConverter
    {
        private const float kminimumSuccess = 80f;
        private const float kCriticalSuccess = 95f;
        private const float kCriticalFailure = 33f;
        private const float kDefaultHoursPerCycle = 1.0f;

        //Result messages for lastAttempt
        protected string attemptCriticalFail = "Critical Failure";
        protected string attemptCriticalSuccess = "Critical Success";
        protected string attemptFail = "Fail";
        protected string attemptSuccess = "Success";
        protected string requiredResource = "Requires ";
        protected string needCrew = "Missing {0} Crew";

        public static bool showResults = true;
        public static bool repairsRequireResources;
        public static bool partsCanBreak;
        public static bool requireSkillCheck;

        [KSPField]
        public int crewsRequired = 0;

        [KSPField]
        public bool checkCrewsWholeVessel;

        [KSPField]
        public float minimumSuccess;

        [KSPField]
        public float criticalSuccess;

        [KSPField]
        public float criticalFail;

        [KSPField]
        public double hoursPerCycle;

        [KSPField(isPersistant = true)]
        public double cycleStartTime;

        [KSPField(guiActive = true, guiName = "Progress", isPersistant = true)]
        public string progress = string.Empty;

        [KSPField(guiActive = true, guiName = "Last Attempt", isPersistant = true)]
        public string lastAttempt = string.Empty;

        [KSPField(isPersistant = true)]
        public bool showGUI = true;

        public double elapsedTime;
        protected float totalCrewSkill = -1.0f;
        protected double secondsPerCycle = 0f;

        public string GetMissingResource()
        {
            PartResourceDefinition definition;
            Dictionary<string, PartResource> resourceMap = new Dictionary<string, PartResource>();

            foreach (PartResource res in this.part.Resources)
            {
                resourceMap.Add(res.resourceName, res);
            }

            //If we have required resources, make sure we have them.
            if (reqList.Count > 0)
            {
                foreach (ResourceRatio resRatio in reqList)
                {
                    //Do we have a definition?
                    definition = ResourceHelper.DefinitionForResource(resRatio.ResourceName);
                    if (definition == null)
                    {
                        return resRatio.ResourceName;
                    }

                    //Do we have the resource aboard?
                    if (resourceMap.ContainsKey(resRatio.ResourceName) == false)
                    {
                        return resRatio.ResourceName;
                    }

                    //Do we have enough?
                    if (resourceMap[resRatio.ResourceName].amount < resRatio.Ratio)
                    {
                        return resRatio.ResourceName;
                    }
                }
            }

            return null;
        }

        [KSPEvent(guiName = "Start Converter", guiActive = true)]
        public virtual void StartConverter()
        {
            string absentResource = GetMissingResource();

            //Do we have enough crew?
            if (hasMinimumCrew() == false)
            {
                return;
            }

            //If we have required resources, make sure we have them.
            if (!string.IsNullOrEmpty(absentResource))
            {
                status = requiredResource + absentResource;
                StopResourceConverter();
                return;
            }

            StartResourceConverter();
            cycleStartTime = Planetarium.GetUniversalTime();
            lastUpdateTime = cycleStartTime;
            elapsedTime = 0.0f;
            Events["StartConverter"].guiActive = false;
            Events["StopConverter"].guiActive = true;
        }

        [KSPEvent(guiName = "Stop Converter", guiActive = true)]
        public virtual void StopConverter()
        {
            StopResourceConverter();
            progress = "None";
            Events["StartConverter"].guiActive = true;
            Events["StopConverter"].guiActive = false;
        }

        public override void OnStart(StartState state)
        {
            base.OnStart(state);
            Events["StartResourceConverter"].guiActive = false;
            Events["StopResourceConverter"].guiActive = false;

            //Setup
            progress = "None";
            if (hoursPerCycle == 0f)
                hoursPerCycle = kDefaultHoursPerCycle;

            Events["StartConverter"].guiName = StartActionName;
            Events["StopConverter"].guiName = StopActionName;
            if (showGUI)
            {
                if (ModuleIsActive())
                {
                    Events["StartConverter"].guiActive = false;
                    Events["StopConverter"].guiActive = true;
                }
                else
                {
                    Events["StartConverter"].guiActive = true;
                    Events["StopConverter"].guiActive = false;
                }
            }
            else
            {
                Events["StartConverter"].guiActive = false;
                Events["StopConverter"].guiActive = false;
            }

            if (minimumSuccess == 0)
                minimumSuccess = kminimumSuccess;
            if (criticalSuccess == 0)
                criticalSuccess = kCriticalSuccess;
            if (criticalFail == 0)
                criticalFail = kCriticalFailure;

            //Check minimum crew
            hasMinimumCrew();
        }

        protected override void PostProcess(ConverterResults result, double deltaTime)
        {
            Events["StartResourceConverter"].guiActive = false;
            Events["StopResourceConverter"].guiActive = false;

            if (FlightGlobals.ready == false)
                return;
            if (HighLogic.LoadedSceneIsFlight == false)
                return;
            if (ModuleIsActive() == false)
                return;
            if (this.part.vessel.IsControllable == false)
            {
                StopConverter();
                return;
            }
            if (hoursPerCycle == 0f)
                return;

            //Make sure we have the minimum crew
            if (hasMinimumCrew() == false)
                return;

            //Now run the base converter stuff
            base.PostProcess(result, deltaTime);

            if (cycleStartTime == 0f)
            {
                cycleStartTime = Planetarium.GetUniversalTime();
                lastUpdateTime = cycleStartTime;
                elapsedTime = 0.0f;
                return;
            }

            //Calculate the crew skill and seconds of research per cycle.
            //Thes values can change if the player swaps out crew.
            totalCrewSkill = GetTotalCrewSkill();
            secondsPerCycle = GetSecondsPerCycle();

            //Calculate elapsed time
            elapsedTime = Planetarium.GetUniversalTime() - cycleStartTime;

            //Calculate progress
            CalculateProgress();

            //If we've completed our research cycle then perform the analyis.
            float completionRatio = (float)(elapsedTime / secondsPerCycle);
            if (completionRatio > 1.0f)
            {
                int cyclesSinceLastUpdate = Mathf.RoundToInt(completionRatio);
                int currentCycle;
                for (currentCycle = 0; currentCycle < cyclesSinceLastUpdate; currentCycle++)
                {
                    PerformAnalysis();

                    //Reset start time
                    cycleStartTime = Planetarium.GetUniversalTime();
                }
            }

            //If we're missing resources then update status
            if (result.Status.ToLower().Contains("missing"))
                status = result.Status;
        }

        public virtual void SetGuiVisible(bool isVisible)
        {
            Events["StartResourceConverter"].guiActive = false;
            Events["StartResourceConverter"].guiActiveEditor = false;
            Events["StartResourceConverter"].guiActiveUnfocused = false;
            Events["StopResourceConverter"].guiActive = false;
            Events["StopResourceConverter"].guiActiveEditor = false;
            Events["StopResourceConverter"].guiActiveUnfocused = false;

            Fields["lastAttempt"].guiActive = isVisible;
            Fields["lastAttempt"].guiActiveEditor = isVisible;
            Fields["progress"].guiActive = isVisible;
            Fields["progress"].guiActiveEditor = isVisible;
            Fields["status"].guiActive = isVisible;

            if (isVisible)
            {
                if (ModuleIsActive())
                {
                    Events["StartConverter"].guiActive = false;
                    Events["StartConverter"].guiActiveUnfocused = false;
                    Events["StartConverter"].guiActiveEditor = false;
                    Events["StopConverter"].guiActive = true;
                    Events["StopConverter"].guiActiveUnfocused = true;
                    Events["StopConverter"].guiActiveEditor = true;
                }

                else
                {
                    Events["StartConverter"].guiActive = true;
                    Events["StartConverter"].guiActiveUnfocused = true;
                    Events["StartConverter"].guiActiveEditor = true;
                    Events["StopConverter"].guiActive = false;
                    Events["StopConverter"].guiActiveUnfocused = false;
                    Events["StopConverter"].guiActiveEditor = false;
                }
            }

            else
            {
                Events["StartConverter"].guiActive = false;
                Events["StartConverter"].guiActiveUnfocused = false;
                Events["StartConverter"].guiActiveEditor = false;
                Events["StopConverter"].guiActive = false;
                Events["StopConverter"].guiActiveUnfocused = false;
                Events["StopConverter"].guiActiveEditor = false;
            }
        }

        public virtual void CalculateProgress()
        {
            //Get elapsed time (seconds)
            progress = string.Format("{0:f1}%", ((elapsedTime / secondsPerCycle) * 100));
        }

        public virtual float GetTotalCrewSkill()
        {
            float totalSkillPoints = 0f;

            if (this.part.CrewCapacity == 0)
                return 0f;

            foreach (ProtoCrewMember crewMember in this.part.protoModuleCrew)
            {
                if (crewMember.experienceTrait.TypeName == Specialty)
                    totalSkillPoints += crewMember.experienceTrait.CrewMemberExperienceLevel();
            }

            return totalSkillPoints;
        }

        public virtual double GetSecondsPerCycle()
        {
            return hoursPerCycle * 3600;
        }

        public virtual void PerformAnalysis()
        {
            float analysisRoll = performAnalysisRoll();

            if (analysisRoll <= criticalFail)
                onCriticalFailure();

            else if (analysisRoll >= criticalSuccess)
                onCriticalSuccess();

            else if (analysisRoll >= minimumSuccess)
                onSuccess();

            else
                onFailure();

        }

        protected virtual float performAnalysisRoll()
        {
            float roll = 0.0f;

            //Roll 3d6 to approximate a bell curve, then convert it to a value between 1 and 100.
            roll = UnityEngine.Random.Range(1, 6);
            roll += UnityEngine.Random.Range(1, 6);
            roll += UnityEngine.Random.Range(1, 6);
            roll *= 5.5556f;

            //Factor in crew
            roll += totalCrewSkill;

            //Done
            return roll;
        }

        protected virtual bool hasMinimumCrew()
        {
            int crewCount;

            //Do we have enough crew?
            if (crewsRequired > 0)
            {
                if (checkCrewsWholeVessel)
                {
                    crewCount = this.part.vessel.GetCrewCount();

                    if (crewCount >= crewsRequired)
                    {
                        return true;
                    }
                    else
                    {
                        status = string.Format(needCrew, crewsRequired - crewCount);
                        return false;
                    }
                }

                crewCount = this.part.protoModuleCrew.Count;

                if (crewsRequired > crewCount)
                {
                    status = string.Format(needCrew, crewsRequired - crewCount);
                    return false;
                }
            }

            return true;
        }

        protected virtual void onCriticalFailure()
        {
            lastAttempt = attemptCriticalFail;
        }

        protected virtual void onCriticalSuccess()
        {
            lastAttempt = attemptCriticalSuccess;
        }

        protected virtual void onFailure()
        {
            lastAttempt = attemptFail;
        }

        protected virtual void onSuccess()
        {
            lastAttempt = attemptSuccess;
        }

        public virtual void Log(object message)
        {
            Debug.Log(this.ClassName + " [" + this.GetInstanceID().ToString("X")
                + "][" + Time.time.ToString("0.0000") + "]: " + message);
        }
    }
}
