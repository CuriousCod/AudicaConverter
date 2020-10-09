using AudicaTools;
using Newtonsoft.Json.Schema;
using osutoaudica;
using OsuTypes;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.Design.Serialization;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;

namespace AudicaConverter
{
    class Program
    {
        public static string FFMPEGNAME = @"\ffmpeg.exe";
        public static string OGG2MOGGNAME = @"\ogg2mogg.exe";
        public static string workingDirectory = Path.GetDirectoryName(Process.GetCurrentProcess().MainModule.FileName);

        static void Main(string[] args)
        {
            Config.Init();
            foreach (var item in args)
            {
                if(item.Contains(".osz")) ConversionProcess.ConvertToAudica(item);
            }
            //ConversionProcess.ConvertToAudica(@"C:\audica\Repos\AudicaConverter\bin\Release\netcoreapp3.1\40218 Momoiro Clover - Pinky Jones (TV Size).osz");
        }
    }

    public class ConversionProcess
    {
        public static bool snapNotes = false;
        public static void ConvertToAudica(string filePath)
        {
            Console.Clear();
            var osz = new OSZ(filePath);
            var audica = new Audica(@$"{Program.workingDirectory}\template.audica");
            Console.WriteLine($"{osz.osufiles[0].metadata.artist} - {osz.osufiles[0].metadata.title}" +
                $"\nMapped by {osz.osufiles[0].metadata.creator}" +
                $"\nFound {osz.osufiles.Count} difficulties");

            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("\n\nSelect Conversion Mode: \n[1] Convert audio/timing only.\n[2] Convert everything");
            Console.ForegroundColor = ConsoleColor.Gray;
            int convertMode = int.Parse(Console.ReadLine());

            if (convertMode == 2)
            {
                Console.Clear();
                SortOSZ(osz);
                int expert = AskDifficulty(osz, "Expert");
                int advanced = AskDifficulty(osz, "Advanced");
                int standard = AskDifficulty(osz, "Standard");
                int beginner = AskDifficulty(osz, "Beginner");

                Console.Clear();
                Console.WriteLine("Converting...");
                ConvertSongToOGG(ref osz, audica, expert);
                audica.expert = expert == 404 ? null : ConvertToAudica(osz.osufiles[expert]);
                audica.advanced = advanced == 404 ? null : ConvertToAudica(osz.osufiles[advanced]);
                audica.moderate = standard == 404 ? null : ConvertToAudica(osz.osufiles[standard]);
                audica.beginner = beginner == 404 ? null : ConvertToAudica(osz.osufiles[beginner]);

            }
            else
            {
                audica.expert = null;
                audica.advanced = null;
                audica.moderate = null;
                audica.beginner = null;
                ConvertSongToOGG(ref osz, audica);
            }

            ConvertMetadata(osz, audica);

            
            //Convert tempos
            var tempList = new List<TempoData>();
            foreach (var timingPoint in osz.osufiles[0].timingPoints)
            {
                tempList.Add(new TempoData((int)timingPoint.audicaTick, TempoData.MicrosecondsPerQuarterNoteFromBPM(60000 / timingPoint.beatTime)));
            }
            audica.tempoData = tempList;

            //at the end
            if (!Directory.Exists(@$"{Program.workingDirectory}\audicaFiles")) Directory.CreateDirectory(@$"{Program.workingDirectory}\audicaFiles");
            audica.Export(@$"{Program.workingDirectory}\audicaFiles\{audica.desc.songID}.audica");
        }

        private static void SortOSZ(OSZ osz)
        {
            var templist = osz.osufiles.OrderBy(o => o.hitObjects.Count).ToList();
            osz.osufiles = templist;
        }

        private static void ConvertMetadata(OSZ osz, Audica audica)
        {
            string mapperName = Config.parameters.customMapperName == "" ? RemoveSpecialCharacters(osz.osufiles[0].metadata.creator) : RemoveSpecialCharacters(Config.parameters.customMapperName);
            audica.desc.title = osz.osufiles[0].metadata.title;
            audica.desc.artist = osz.osufiles[0].metadata.artist;
            audica.desc.author = mapperName;
            audica.desc.songID = RemoveSpecialCharacters(osz.osufiles[0].metadata.title) + "-" + mapperName;
            audica.desc.previewStartSeconds = (float)osz.osufiles[0].general.previewTime / 1000f;
            audica.desc.fxSong = "";
        }

        private static int AskDifficulty(OSZ osz, string difficultyName)
        {
            Console.WriteLine($"\n\nSelect {difficultyName} difficulty[Leave empty for none]:");
            Console.ForegroundColor = ConsoleColor.Yellow;
            for (int i = 0; i < osz.osufiles.Count; i++)
            {
                Console.WriteLine($"\n[{i}]{osz.osufiles[i].metadata.version} [{osz.osufiles[i].hitObjects.Count} objects]");
            }
            Console.ForegroundColor = ConsoleColor.Gray;
            string userInput = Console.ReadLine();
            Console.Clear();
            if (userInput == "") return 404;// User hasn't picked a difficulty
            else
            {
                int difficulty = int.Parse(userInput);
                return difficulty;
            }
            
        }

        private static void ConvertSongToOGG(ref OSZ osz, Audica audica, int diffIndex = 0)
        {
            osufile difficulty = osz.osufiles[diffIndex];
            string audioFileName = difficulty.general.audioFileName;
            string tempDirectory = Program.workingDirectory + @"\AudicaConverterTemp\";
            string tempAudioPath = tempDirectory + @"audio.mp3";
            string tempOggPath = tempDirectory + @"tempogg.ogg";
            string tempMoggPath = tempDirectory + @"tempMogg.mogg";

            float paddingTime = 0f;
            if (difficulty.hitObjects.Count > 0)
            {
                HitObject firstHitObject = difficulty.hitObjects[0];
                if (firstHitObject.time < Config.parameters.introPadding)
                {
                    paddingTime = Config.parameters.introPadding - firstHitObject.time;
                }
            }

            if (paddingTime > 0f)
            {
                foreach (var osuDifficulty in osz.osufiles)
                {
                    foreach (var timingPoint in osuDifficulty.timingPoints)
                    {
                        if (timingPoint.ms > 0f)
                        {
                            timingPoint.ms += paddingTime;
                        }
                    }
                    foreach (var timingPoint in osuDifficulty.timingPoints)
                    {
                        if (timingPoint.ms > 0f)
                        {
                            timingPoint.audicaTick += OsuUtility.MsToTick(paddingTime, osuDifficulty.timingPoints);
                        }
                    }

                    foreach (var hitObject in osuDifficulty.hitObjects)
                    {
                        hitObject.time += paddingTime;
                        hitObject.audicaTick += OsuUtility.MsToTick(paddingTime, osuDifficulty.timingPoints);
                        hitObject.endTime += paddingTime;
                        hitObject.audicaEndTick += OsuUtility.MsToTick(paddingTime, osuDifficulty.timingPoints);
                    }
                    osuDifficulty.general.previewTime += (int)paddingTime;
                } 
            }


            if (Directory.Exists(tempDirectory)) Directory.Delete(tempDirectory, true);

            Directory.CreateDirectory(tempDirectory);

            ZipArchive zip = ZipFile.OpenRead(osz.oszFilePath);
            zip.GetEntry(audioFileName).ExtractToFile(tempAudioPath);

            Process ffmpeg = new Process();
            ffmpeg.StartInfo.FileName = Program.workingDirectory + Program.FFMPEGNAME;
            ffmpeg.StartInfo.WindowStyle = ProcessWindowStyle.Normal;
            ffmpeg.StartInfo.UseShellExecute = false;
            ffmpeg.StartInfo.RedirectStandardOutput = true;

            Process ogg2mogg = new Process();
            ogg2mogg.StartInfo.FileName = Program.workingDirectory + Program.OGG2MOGGNAME;
            ogg2mogg.StartInfo.WindowStyle = ProcessWindowStyle.Normal;
            ogg2mogg.StartInfo.UseShellExecute = false;
            ogg2mogg.StartInfo.RedirectStandardOutput = true;

            ogg2mogg.StartInfo.Arguments = $"\"{tempOggPath}\" \"{tempMoggPath}\"";

            string paddingString = paddingTime > 0 ? $"-af \"adelay = {paddingTime} | {paddingTime}\"" : "";


            ffmpeg.StartInfo.Arguments = $"-y -i \"{tempAudioPath}\" -hide_banner -loglevel panic -ab 256k -ss 0.025 {paddingString} -map 0:a \"{tempOggPath}\"";
            ffmpeg.Start();
            ffmpeg.WaitForExit();

            ogg2mogg.Start();
            ogg2mogg.WaitForExit();

            var ms = new MemoryStream(File.ReadAllBytes(tempMoggPath));
            audica.song = new Mogg(ms);

            Directory.Delete(tempDirectory, true);
        }

        public static Difficulty ConvertToAudica(osufile osufile)
        {
            var diff = new Difficulty();
            diff.cues = new List<Cue>();
            var handColorHandler = new HandColorHandler();
            HitObject prevRightHitObject = null;
            HitObject prevLeftHitObject = null;


            if (Config.parameters.convertSliderEnds) RunSliderSplitPass(ref osufile.hitObjects, osufile.timingPoints);
            if (Config.parameters.convertSustains) RunSustainPass(ref osufile.hitObjects, osufile.timingPoints);
            if (Config.parameters.convertChains) RunChainPass(ref osufile.hitObjects, osufile.timingPoints);
            ResetEndTimesAndPos(ref osufile.hitObjects);

            // do conversion stuff here
            for (int i = 0; i < osufile.hitObjects.Count; i++)
            {
                var hitObject = osufile.hitObjects[i];
                var audicaDataPos = OsuUtility.GetAudicaPosFromHitObject(hitObject);
                var gridOffset = ConversionProcess.snapNotes ? new Cue.GridOffset() : audicaDataPos.offset;
                int tickLength = 120;

                if (hitObject.audicaBehavior == 5)
                {
                    hitObject.audicaHandType = osufile.hitObjects[i - 1].audicaHandType;
                }
                else
                {
                    if (hitObject.audicaBehavior == 3)
                    {
                        tickLength = (int)hitObject.audicaEndTick - (int)hitObject.audicaTick;
                    }

                    HitObject nextHitObject = null; //Next non chain-link hitObject
                    for (int j = 1; nextHitObject==null && i+j < osufile.hitObjects.Count; j++)
                    {
                        if (osufile.hitObjects[i + j].audicaBehavior != 5) nextHitObject = osufile.hitObjects[i + j];
                    }
                    hitObject.audicaHandType = handColorHandler.GetHandType(hitObject, prevRightHitObject, prevLeftHitObject, nextHitObject);
                    if (hitObject.audicaHandType == 1) prevRightHitObject = hitObject;
                    else prevLeftHitObject = hitObject;
                }

                var cue = new Cue
                    (
                        (int)hitObject.audicaTick,
                        tickLength,
                        audicaDataPos.pitch,
                        OsuUtility.GetVelocityForObject(hitObject),
                        gridOffset,
                        0f,
                        hitObject.audicaHandType,
                        hitObject.audicaBehavior
                    );
                hitObject.audicaCue = cue;
                diff.cues.Add(cue);
            }


            if (Config.parameters.snapNotes) SnapNormalTargets(ref diff.cues);
            RunStackDistributionPass(ref diff.cues);
            
            if(Config.parameters.useChainSounds) RunHitsoundPass(ref diff.cues);

            return diff;
        }

        private static void RunSliderSplitPass(ref List<HitObject> hitObjects, List<TimingPoint> timingPoints)
        {
            int hitObjectsOrgCount = hitObjects.Count;
            for (int i = 0; i < hitObjectsOrgCount - 1; i++)
            {
                HitObject hitObject = hitObjects[i];
                HitObject nextHitObject = hitObjects[i + 1];

                //Add a hitObject for sliders if the end is on beat and the next target is within 1/12th of the slider end.
                if ((hitObject.type == 2 || hitObject.type == 6) && OsuUtility.ticksSinceLastTimingPoint(hitObject.audicaEndTick, timingPoints) % 240f == 0f &&
                    nextHitObject.audicaTick - hitObject.audicaEndTick <= 160f)
                {
                    HitObject newHitObject = new HitObject
                    (
                        hitObject.repeats % 2 == 1 ? hitObject.endX : hitObject.x,
                        hitObject.repeats % 2 == 1 ? hitObject.endY : hitObject.y,
                        hitObject.endTime,
                        1,
                        hitObject.endHitsound,
                        0f,
                        0
                    );

                    newHitObject.endTime = newHitObject.time;
                    newHitObject.endX = newHitObject.x;
                    newHitObject.endY = newHitObject.y;
                    newHitObject.audicaTick = newHitObject.audicaEndTick = hitObject.audicaEndTick;
                    hitObjects.Add(newHitObject);
                }
            }
            hitObjects.Sort((ho1, ho2) => ho1.time.CompareTo(ho2.time));
        }

        private static void RunHitsoundPass(ref List<Cue> cues)
        {
            foreach (var cue in cues)
            {
                switch (cue.behavior)
                {
                    case 4:
                        cue.velocity = 1;
                        break;
                    case 5:
                        cue.velocity = 2;
                        break;
                    case 6:
                        cue.velocity = 3; //melee for future purposes
                        break;

                    default:
                        break;
                }
            }
        }

        private static void SnapNormalTargets(ref List<Cue> cues)
        {
            foreach (Cue cue in cues)
            {
                if(cue.behavior == 0)
                {
                    cue.gridOffset = new Cue.GridOffset();
                }
            }
        }

        private static void RunChainPass(ref List<HitObject> hitObjects, List<TimingPoint> timingPoints)
        {

            HitObject prevChainHeadHitObject = null;

            for (int i = 0; i < hitObjects.Count; i++)
            {
                HitObject prevHitObject = i > 0 ? hitObjects[i - 1] : null;
                HitObject nextHitObject = i + 1 < hitObjects.Count ? hitObjects[i + 1] : null;
                HitObject nextNextHitObject = i + 2 < hitObjects.Count ? hitObjects[i + 2] : null;
                HitObject currentHitObject = hitObjects[i];

                bool isIgnoredChainEnd = Config.parameters.ignoreSlidersForChainConvert && (currentHitObject.type == 2 || currentHitObject.type == 6) &&
                    (nextHitObject == null || nextHitObject.time - currentHitObject.time > Config.parameters.chainTimeThres) || Config.parameters.ignoreSustainsForChainConvert && currentHitObject.audicaBehavior == 3;
                bool nextIsIgnoredChainEnd = nextHitObject == null || Config.parameters.ignoreSlidersForChainConvert && (nextHitObject.type == 2 || nextHitObject.type == 6) &&
                    (nextNextHitObject == null || nextNextHitObject.time - nextHitObject.time > Config.parameters.chainTimeThres) || Config.parameters.ignoreSustainsForChainConvert && nextHitObject.audicaBehavior == 3;

                if (isIgnoredChainEnd)
                    continue;

                if ((prevHitObject == null || currentHitObject.time - prevHitObject.time > Config.parameters.chainTimeThres) && nextHitObject != null &&
                    nextHitObject.time - currentHitObject.time <= Config.parameters.chainTimeThres && !nextIsIgnoredChainEnd)
                {
                    currentHitObject.audicaBehavior = 4;
                    prevChainHeadHitObject = currentHitObject;
                }
                else if (prevHitObject != null && currentHitObject.time - prevHitObject.time <= Config.parameters.chainTimeThres)
                {   
                    if (currentHitObject.time - prevChainHeadHitObject.time > Config.parameters.chainTimeThres && currentHitObject.audicaTick - prevChainHeadHitObject.audicaTick >= Config.parameters.chainSwitchFrequency && nextHitObject != null &&
                        nextHitObject.time - currentHitObject.time <= Config.parameters.chainTimeThres && OsuUtility.ticksSinceLastTimingPoint(currentHitObject.audicaTick, timingPoints) % Config.parameters.chainSwitchFrequency == 0 && !nextIsIgnoredChainEnd)
                    {
                        currentHitObject.audicaBehavior = 4;
                        prevChainHeadHitObject = currentHitObject;
                    }
                    else
                    {
                        currentHitObject.audicaBehavior = 5;
                        prevChainHeadHitObject.endTime = currentHitObject.time;
                        prevChainHeadHitObject.audicaEndTick = currentHitObject.audicaEndTick;
                        prevChainHeadHitObject.endX = currentHitObject.x;
                        prevChainHeadHitObject.endY = currentHitObject.y;
                    }
                }
            }
        }

        private static void RunSustainPass(ref List<HitObject> hitObjects, List<TimingPoint> timingPoints)
        {
            for (int i = 0; i < hitObjects.Count; i++)
            {
                HitObject currentHitObject = hitObjects[i];
                HitObject nextHitObject = i + 1 < hitObjects.Count ? hitObjects[i + 1] : null;

                if (currentHitObject.audicaBehavior == 4) continue;

                //Extend duration to next target if next target is on beat and within extension time
                if (nextHitObject != null && nextHitObject.audicaTick - currentHitObject.audicaEndTick <= Config.parameters.sustainExtension)
                {
                    if (OsuUtility.ticksSinceLastTimingPoint(nextHitObject.audicaTick, timingPoints) % 480f == 0f)
                    {
                        currentHitObject.endTime = nextHitObject.time;
                        currentHitObject.audicaEndTick = nextHitObject.audicaTick;
                    }
                }

                if (currentHitObject.audicaEndTick - currentHitObject.audicaTick >= Config.parameters.minSustainLength)
                {
                    currentHitObject.audicaBehavior = 3;
                }
            }
        }

        private static void ResetEndTimesAndPos(ref List<HitObject> hitObjects)
        {
            foreach (HitObject hitObject in hitObjects)
            {
                if (hitObject.audicaBehavior != 3 && hitObject.audicaBehavior != 4)
                {
                    hitObject.endTime = hitObject.time;
                    hitObject.audicaEndTick = hitObject.audicaTick;
                }

                if (hitObject.audicaBehavior != 4)
                {
                    hitObject.endX = hitObject.x;
                    hitObject.endY = hitObject.y;
                }
            }
        }

        private class TargetStack
        {
            public Cue stackStartCue;
            public List<Cue> tailCues;
            public float stackMovementSpeed;
            public float lastStackTick;
            public OsuUtility.Coordinate2D direction;

            public TargetStack()
            {
                tailCues = new List<Cue>();
            }
        }

        private static void RunStackDistributionPass(ref List<Cue> cues)
        {
            float stackItemDistance = Config.parameters.stackItemDistance; //Offset for stack items. Time proportionate distancing is used through the stack based on getting this distance between first and second item in stack
            float stackMaxDistance = Config.parameters.stackMaxDistance;
            float stackResetTime = Config.parameters.stackResetTime;

            List<TargetStack> allStacks = new List<TargetStack>();
            List<TargetStack> activeStacks = new List<TargetStack>();

            for (int i = 0; i < cues.Count; i++)
            {
                Cue currentCue = cues[i];
                Cue prevCue = i - 1 >= 0 ? cues[i - 1] : null;
                Cue nextCue = i + 1 < cues.Count ? cues[i + 1] : null;

                //Remove unactive stacks
                for (int j = activeStacks.Count - 1; j >= 0; j--)
                {
                    if (currentCue.tick - activeStacks[j].lastStackTick >= stackResetTime)
                        activeStacks.RemoveAt(j);
                }

                //Check if target fits in a currently active stack, if not create a stack for this target
                TargetStack stack = activeStacks.Find(s => OsuUtility.CuesPosEquals(currentCue, s.stackStartCue));
                if (stack == null)
                {
                    TargetStack newStack = new TargetStack();
                    newStack.stackStartCue = currentCue;
                    newStack.lastStackTick = currentCue.tick;
                    allStacks.Add(newStack);
                    activeStacks.Add(newStack);
                    continue;
                }

                //Don't distribute of target is a part of a stream
                if (prevCue != null && !OsuUtility.CuesPosEquals(prevCue, currentCue) && currentCue.tick - prevCue.tick <= 140 ||
                    nextCue != null && !OsuUtility.CuesPosEquals(nextCue, currentCue) && nextCue.tick - currentCue.tick <= 140)
                    continue;

                OsuUtility.Coordinate2D currentCuePos = OsuUtility.GetPosFromCue(currentCue);
                if (stack.direction.x == 0f && stack.direction.y == 0f)
                {

                    OsuUtility.Coordinate2D? dirToNextCue = null;
                    for (int j = 1; j + i < cues.Count; j++)
                    {
                        Cue otherCue = cues[i + j];
                        if (!OsuUtility.CuesPosEquals(currentCue, otherCue))
                        {
                            OsuUtility.Coordinate2D otherCuePos = OsuUtility.GetPosFromCue(otherCue);
                            dirToNextCue = new OsuUtility.Coordinate2D(otherCuePos.x - currentCuePos.x, otherCuePos.y - currentCuePos.y);
                            break;
                        }
                    }

                    OsuUtility.Coordinate2D? dirFromPrevCue = null;
                    for (int j = 1; j <= i; j++)
                    {
                        Cue otherCue = cues[i - j];
                        if (!OsuUtility.CuesPosEquals(currentCue, otherCue) &&
                            otherCue.tick < stack.stackStartCue.tick)
                        {
                            OsuUtility.Coordinate2D otherCuePos = OsuUtility.GetPosFromCue(otherCue);
                            dirFromPrevCue = new OsuUtility.Coordinate2D(currentCuePos.x - otherCuePos.x, currentCuePos.y - otherCuePos.y);
                            break;
                        }
                    }

                    OsuUtility.Coordinate2D direction = new OsuUtility.Coordinate2D(0f, -1f);
                    if (dirFromPrevCue != null) direction = (OsuUtility.Coordinate2D)dirFromPrevCue;
                    else if (dirToNextCue != null) direction = (OsuUtility.Coordinate2D)dirToNextCue;

                    //normalize
                    float length = (float)Math.Sqrt(direction.x * direction.x + direction.y * direction.y);
                    direction.x /= length;
                    direction.y /= length;

                    stack.direction = direction;
                }
                stack.tailCues.Add(currentCue);
                stack.lastStackTick = currentCue.tick;
            }

            foreach (TargetStack stack in allStacks)
            {
                if (stack.tailCues.Count == 0)
                    continue;
                stack.stackMovementSpeed = Math.Min(stackItemDistance / (stack.tailCues[0].tick - stack.stackStartCue.tick), stackMaxDistance / (stack.tailCues[stack.tailCues.Count - 1].tick - stack.stackStartCue.tick));

                foreach (Cue cue in stack.tailCues)
                {
                    OsuUtility.Coordinate2D cuePos = OsuUtility.GetPosFromCue(cue);
                    OsuUtility.Coordinate2D newPos = new OsuUtility.Coordinate2D(cuePos.x + stack.direction.x * stack.stackMovementSpeed * (cue.tick - stack.stackStartCue.tick),
                        cuePos.y + stack.direction.y * stack.stackMovementSpeed * (cue.tick - stack.stackStartCue.tick));

                    OsuUtility.AudicaDataPos newAudicaPos = OsuUtility.CoordinateToAudicaPos(newPos);

                    cue.pitch = newAudicaPos.pitch;
                    cue.gridOffset = newAudicaPos.offset;

                }
            }
        }

        public static string RemoveSpecialCharacters(string str)
        {
            StringBuilder sb = new StringBuilder();
            foreach (char c in str)
            {
                if(sb.Length < 14)
                {
                    if ((c >= '0' && c <= '9') || (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || c == '.' || c == '_')
                    {
                        sb.Append(c);
                    }
                }
                
            }
            return sb.ToString().Replace(".", "");
        }
    }

    public class OSZ
    {
        public string oszFilePath;
        
        public List<osufile> osufiles = new List<osufile>();
        public OSZ(string filePath)
        {
            oszFilePath = filePath;
            ZipArchive zip = ZipFile.OpenRead(filePath);
            foreach (var entry in zip.Entries)
            {
                if (entry.Name.Contains(".osu"))
                {
                    osufiles.Add(new osufile(entry.Open()));   
                }
            }
        }
    }
}


