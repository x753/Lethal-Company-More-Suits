using System;
using System.Collections.Generic;
using BepInEx.Logging;
using UnityEngine.SceneManagement;

namespace MoreSuits
{
    public abstract class SuitSorter
    {
        private static SuitSorter currentSorter;

        public static SuitSorter CurrentSorter
        {
            get => currentSorter;
            set
            {
                if(!IsSorterRegistered(value)) return;
                bool isGameScene = SceneManager.GetActiveScene().name == "SampleSceneRelay";
                SuitSorter previousSorter = currentSorter;
                currentSorter = value;
                if(previousSorter != null)
                {
                    try { previousSorter.SwitchedAwayFromSorter(isGameScene); }
                    catch (Exception e) { previousSorter.Logger.LogError(e); }
                }
                try{currentSorter.SwitchedToSorter(isGameScene);}
                catch(Exception e){currentSorter.Logger.LogError(e);}
            }
        }

        public bool IsCurrentSorter => CurrentSorter == this;
        public SuitLogger Logger { get; internal set; }

        internal static ManualLogSource rootLogger;
        internal static readonly Dictionary<string, SuitSorter> PossibleSorters = new Dictionary<string, SuitSorter>();

        public static bool IsSorterRegistered(string identifier) => PossibleSorters.ContainsKey(identifier);
        public static bool IsSorterRegistered(SuitSorter suitSorter) => PossibleSorters.ContainsValue(suitSorter);

        public static void RegisterSorter(string identifier, SuitSorter suitSorter, bool ignoreConfigCheck = false)
        {
            if(PossibleSorters.ContainsKey(identifier)) return;
            PossibleSorters.Add(identifier, suitSorter);
            suitSorter.Logger = new SuitLogger(identifier, rootLogger);
            try{suitSorter.OnRegistered();}catch(Exception e){ suitSorter.Logger.LogError(e); }
            if(ignoreConfigCheck) return;
            if (MoreSuitsMod.SelectedSorter == identifier)
                CurrentSorter = suitSorter;
        }

        public static void DeregisterSorter(string identifier)
        {
            if(!PossibleSorters.ContainsKey(identifier)) return;
            SuitSorter suitSorter = PossibleSorters[identifier];
            if (suitSorter == CurrentSorter)
                CurrentSorter = MoreSuitsMod._none;
            try{suitSorter.OnDeregistered();}catch(Exception e){ suitSorter.Logger.LogError(e); }
        }

        public static bool TryGetSorterByIdentifier(string identifier, out SuitSorter suitSorter) =>
            PossibleSorters.TryGetValue(identifier, out suitSorter);

        public static void PatchedPositionSuits(StartOfRound startOfRound) =>
            MoreSuitsMod.StartOfRoundPatch.PositionSuitsOnRackPatch(ref startOfRound);

        public virtual void OnRegistered(){}
        public virtual void SwitchedToSorter(bool isGameScene){}
        public virtual void SwitchedAwayFromSorter(bool isGameScene){}
        public virtual void OnGameSceneLoaded(StartOfRound startOfRound){}
        public virtual void SortSuitRack(StartOfRound startOfRound, UnlockableSuit[] suits){}
        public virtual void OnDeregistered(){}
    }

    public class SuitLogger
    {
        private string suitSorterId;
        private ManualLogSource rootLogger;

        internal SuitLogger(string identifier, ManualLogSource rootLogger)
        {
            suitSorterId = identifier;
            this.rootLogger = rootLogger;
        }

        public void Log(LogLevel level, object data) => rootLogger.Log(level, $"[{suitSorterId}] {data}");
        public void LogFatal(object data) => rootLogger.Log(LogLevel.Fatal, $"[{suitSorterId}] {data}");
        public void LogError(object data) => rootLogger.Log(LogLevel.Error, $"[{suitSorterId}] {data}");
        public void LogWarning(object data) => rootLogger.Log(LogLevel.Warning, $"[{suitSorterId}] {data}");
        public void LogMessage(object data) => rootLogger.Log(LogLevel.Message, $"[{suitSorterId}] {data}");
        public void LogInfo(object data) => rootLogger.Log(LogLevel.Info, $"[{suitSorterId}] {data}");
        public void LogDebug(object data) => rootLogger.Log(LogLevel.Debug, $"[{suitSorterId}] {data}");
    }
}