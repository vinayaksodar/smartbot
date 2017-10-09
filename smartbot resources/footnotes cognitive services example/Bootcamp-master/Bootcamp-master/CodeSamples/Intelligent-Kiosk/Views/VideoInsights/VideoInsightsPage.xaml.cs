﻿// 
// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license.
// 
// Microsoft Cognitive Services: http://www.microsoft.com/cognitive
// 
// Microsoft Cognitive Services Github:
// https://github.com/Microsoft/Cognitive
// 
// Copyright (c) Microsoft Corporation
// All rights reserved.
// 
// MIT License:
// Permission is hereby granted, free of charge, to any person obtaining
// a copy of this software and associated documentation files (the
// "Software"), to deal in the Software without restriction, including
// without limitation the rights to use, copy, modify, merge, publish,
// distribute, sublicense, and/or sell copies of the Software, and to
// permit persons to whom the Software is furnished to do so, subject to
// the following conditions:
// 
// The above copyright notice and this permission notice shall be
// included in all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED ""AS IS"", WITHOUT WARRANTY OF ANY KIND,
// EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
// MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
// NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE
// LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
// OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION
// WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
// 

using IntelligentKioskSample.Controls;
using IntelligentKioskSample.Views.VideoInsights;
using KioskRuntimeComponent;
using Microsoft.ProjectOxford.Common;
using Microsoft.ProjectOxford.Common.Contract;
using Microsoft.ProjectOxford.Vision;
using Newtonsoft.Json.Linq;
using ServiceHelpers;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Serialization;
using Windows.Graphics.Imaging;
using Windows.Media.Editing;
using Windows.Media.Effects;
using Windows.Networking.BackgroundTransfer;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.UI.Popups;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Navigation;

namespace IntelligentKioskSample.Views
{

    [KioskExperience(Title = "Realtime Video Insights", ImagePath = "ms-appx:/Assets/realtimeFromVideo.png", ExperienceType = ExperienceType.Other)]
    public sealed partial class VideoInsightsPage : Page
    {
        private Task processingLoopTask;
        private bool isProcessingLoopInProgress;
        private bool isProcessingPhoto;
        private Guid currentVideoId;

        private Dictionary<Guid, Visitor> peopleInVideo = new Dictionary<Guid, Visitor>();
        private DemographicsData demographics;
        private Dictionary<Guid, int> pendingIdentificationAttemptCount = new Dictionary<Guid, int>();
        private HashSet<int> processedFrames = new HashSet<int>();

        private Dictionary<string, int> tagsInVideo = new Dictionary<string, int>();

        public VideoInsightsPage()
        {
            this.InitializeComponent();

            this.DataContext = this;
        }

        private void StartProcessingLoop()
        {
            this.isProcessingLoopInProgress = true;

            if (this.processingLoopTask == null || this.processingLoopTask.Status != TaskStatus.Running)
            {
                this.processingLoopTask = Task.Run(() => this.ProcessingLoop());
            }
        }

        private async void ProcessingLoop()
        {
            while (this.isProcessingLoopInProgress)
            {
                await this.Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, async () =>
                {
                    if (!this.isProcessingPhoto &&
                        FrameRelayVideoEffect.LatestSoftwareBitmap != null &&
                        (this.videoPlayer.CurrentState == MediaElementState.Playing || this.videoPlayer.CurrentState == MediaElementState.Paused))
                    {
                        this.isProcessingPhoto = true;
                        try
                        {
                            await this.ProcessCurrentVideoFrame();
                        }
                        catch
                        { }
                        finally
                        {
                            this.isProcessingPhoto = false;
                        }
                    }

                    if (this.videoPlayer.CurrentState == MediaElementState.Playing || this.videoPlayer.CurrentState == MediaElementState.Paused)
                    {
                        this.timelineTip.Margin = new Thickness
                        {
                            Left = (this.videoPlayer.Position.TotalSeconds / this.videoPlayer.NaturalDuration.TimeSpan.TotalSeconds) * (this.timelineTipHost.ActualWidth - 20)
                        };
                    }
                });
            }
        }

        private async Task ProcessCurrentVideoFrame()
        {
            int frameNumber = (int)this.videoPlayer.Position.TotalSeconds;

            if (this.processedFrames.Contains(frameNumber))
            {
                return;
            }

            Guid videoIdBeforeProcessing = this.currentVideoId;

            var analyzer = new ImageAnalyzer(await Util.GetPixelBytesFromSoftwareBitmapAsync(FrameRelayVideoEffect.LatestSoftwareBitmap));

            DateTime start = DateTime.Now;

            // Compute Emotion, Age, Gender, Celebrities and Visual Features
            await Task.WhenAll(
                analyzer.DetectEmotionAsync(),
                analyzer.DetectFacesAsync(detectFaceAttributes: true),
                analyzer.AnalyzeImageAsync(true, new VisualFeature[] { VisualFeature.Categories, VisualFeature.Tags }));

            // Compute Face Identification and Unique Face Ids
            await Task.WhenAll(analyzer.IdentifyFacesAsync(), analyzer.FindSimilarPersistedFacesAsync());

            await Task.WhenAll(ProcessPeopleInsightsAsync(analyzer, frameNumber), ProcessVisualFeaturesInsightsAsync(analyzer, frameNumber));

            if (videoIdBeforeProcessing != this.currentVideoId)
            {
                // Media source changed while we were processing. Make sure we are in a clear state again.
                await this.ResetStateAsync();
            }

            debugText.Text = string.Format("Latency: {0:0}ms", (DateTime.Now - start).TotalMilliseconds);

            this.processedFrames.Add(frameNumber);
        }

        private async Task ProcessPeopleInsightsAsync(ImageAnalyzer analyzer, int frameNumber)
        {
            foreach (var item in analyzer.SimilarFaceMatches)
            {
                bool demographicsChanged = false;
                Visitor personInVideo;
                if (this.peopleInVideo.TryGetValue(item.SimilarPersistedFace.PersistedFaceId, out personInVideo))
                {
                    personInVideo.Count++;

                    if (this.pendingIdentificationAttemptCount.ContainsKey(item.SimilarPersistedFace.PersistedFaceId))
                    {
                        // This is a face we haven't identified yet. See how many times we have tried it, if we need to do it again or stop trying
                        if (this.pendingIdentificationAttemptCount[item.SimilarPersistedFace.PersistedFaceId] <= 5)
                        {
                            string personName = GetDisplayTextForPersonAsync(analyzer, item);
                            if (string.IsNullOrEmpty(personName))
                            {
                                // Increment the times we have tried and failed to identify this person
                                this.pendingIdentificationAttemptCount[item.SimilarPersistedFace.PersistedFaceId]++;
                            }
                            else
                            {
                                // Bingo! Let's remove it from the list of pending identifications
                                this.pendingIdentificationAttemptCount.Remove(item.SimilarPersistedFace.PersistedFaceId);

                                VideoTrack existingTrack = (VideoTrack)this.peopleListView.Children.FirstOrDefault(f => (Guid)((FrameworkElement)f).Tag == item.SimilarPersistedFace.PersistedFaceId);
                                if (existingTrack != null)
                                {
                                    existingTrack.DisplayText = string.Format("{0}, {1}", personName, Math.Floor(item.Face.FaceAttributes.Age));
                                }
                            }
                        }
                        else
                        {
                            // Give up
                            this.pendingIdentificationAttemptCount.Remove(item.SimilarPersistedFace.PersistedFaceId);
                        }
                    }
                }
                else
                {
                    // New person... let's catalog it.

                    // Crop the face, enlarging the rectangle so we frame it better
                    double heightScaleFactor = 1.8;
                    double widthScaleFactor = 1.8;
                    Rectangle biggerRectangle = new Rectangle
                    {
                        Height = Math.Min((int)(item.Face.FaceRectangle.Height * heightScaleFactor), FrameRelayVideoEffect.LatestSoftwareBitmap.PixelHeight),
                        Width = Math.Min((int)(item.Face.FaceRectangle.Width * widthScaleFactor), FrameRelayVideoEffect.LatestSoftwareBitmap.PixelWidth)
                    };
                    biggerRectangle.Left = Math.Max(0, item.Face.FaceRectangle.Left - (int)(item.Face.FaceRectangle.Width * ((widthScaleFactor - 1) / 2)));
                    biggerRectangle.Top = Math.Max(0, item.Face.FaceRectangle.Top - (int)(item.Face.FaceRectangle.Height * ((heightScaleFactor - 1) / 1.4)));

                    var croppedImage = await Util.GetCroppedBitmapAsync(analyzer.GetImageStreamCallback, biggerRectangle);

                    if (croppedImage == null || biggerRectangle.Height == 0 && biggerRectangle.Width == 0)
                    {
                        // Couldn't get a shot of this person
                        continue;
                    }

                    demographicsChanged = true;

                    string personName = GetDisplayTextForPersonAsync(analyzer, item);
                    if (string.IsNullOrEmpty(personName))
                    {
                        personName = item.Face.FaceAttributes.Gender;

                        // Add the person to the list of pending identifications so we can try again on some future frames
                        this.pendingIdentificationAttemptCount.Add(item.SimilarPersistedFace.PersistedFaceId, 1);
                    }

                    personInVideo = new Visitor { UniqueId = item.SimilarPersistedFace.PersistedFaceId };
                    this.peopleInVideo.Add(item.SimilarPersistedFace.PersistedFaceId, personInVideo);
                    this.demographics.Visitors.Add(personInVideo);

                    // Update the demographics stats. 
                    this.UpdateDemographics(item);

                    VideoTrack videoTrack = new VideoTrack
                    {
                        Tag = item.SimilarPersistedFace.PersistedFaceId,
                        CroppedFace = croppedImage,
                        DisplayText = string.Format("{0}, {1}", personName, Math.Floor(item.Face.FaceAttributes.Age)),
                        Duration = (int)this.videoPlayer.NaturalDuration.TimeSpan.TotalSeconds,
                    };

                    videoTrack.Tapped += this.TimelineTapped;

                    this.peopleListView.Children.Insert(0, videoTrack);
                }

                // Update the timeline for this person
                VideoTrack track = (VideoTrack)this.peopleListView.Children.FirstOrDefault(f => (Guid)((FrameworkElement)f).Tag == item.SimilarPersistedFace.PersistedFaceId);
                if (track != null)
                {
                    Emotion matchingEmotion = CoreUtil.FindFaceClosestToRegion(analyzer.DetectedEmotion, item.Face.FaceRectangle);
                    if (matchingEmotion == null)
                    {
                        matchingEmotion = new Emotion { Scores = new EmotionScores { Neutral = 1 } };
                    }

                    track.SetVideoFrameState(frameNumber, matchingEmotion.Scores);

                    uint childIndex = (uint)this.peopleListView.Children.IndexOf(track);
                    if (childIndex > 5)
                    {
                        // Bring to towards the top so it becomes visible
                        this.peopleListView.Children.Move(childIndex, 5);
                    }
                }

                if (demographicsChanged)
                {
                    this.ageGenderDistributionControl.UpdateData(this.demographics);
                }

                this.overallStatsControl.UpdateData(this.demographics);
            }
        }

        private async Task ProcessVisualFeaturesInsightsAsync(ImageAnalyzer analyzer, int frameNumber)
        {
            foreach (var tag in analyzer.AnalysisResult.Tags)
            {
                if (this.tagsInVideo.ContainsKey(tag.Name))
                {
                    this.tagsInVideo[tag.Name]++;
                }
                else
                {
                    this.tagsInVideo[tag.Name] = 1;

                    BitmapImage frameBitmap = new BitmapImage();
                    await frameBitmap.SetSourceAsync((await analyzer.GetImageStreamCallback()).AsRandomAccessStream());
                    VideoTrack videoTrack = new VideoTrack
                    {
                        Tag = tag.Name,
                        CroppedFace = frameBitmap,
                        DisplayText = tag.Name,
                        Duration = (int)this.videoPlayer.NaturalDuration.TimeSpan.TotalSeconds,
                    };

                    videoTrack.Tapped += this.TimelineTapped;
                    this.tagsListView.Children.Insert(0, videoTrack);

                    this.FilterFeatureTimeline();
                }

                // Update the timeline for this tag
                VideoTrack track = (VideoTrack)this.tagsListView.Children.FirstOrDefault(f => (string)((FrameworkElement)f).Tag == tag.Name);
                if (track != null)
                {
                    track.SetVideoFrameState(frameNumber, new EmotionScores { Neutral = 1 });

                    uint childIndex = (uint)this.tagsListView.Children.IndexOf(track);
                    if (childIndex > 5)
                    {
                        // Bring towards the top so it becomes visible
                        this.tagsListView.Children.Move(childIndex, 5);
                    }
                }
            }

            this.UpdateTagFilters();
        }

        private void UpdateTagFilters()
        {
            int index = 0;
            List<KeyValuePair<String, int>> tagsGroupedByCountAndSorted = new List<KeyValuePair<String, int>>();
            foreach (var group in this.tagsInVideo.GroupBy(t => t.Value).OrderByDescending(g => g.Key))
            {
                tagsGroupedByCountAndSorted.AddRange(group.OrderBy(g => g.Key));
            }

            foreach (var item in tagsGroupedByCountAndSorted)
            {
                TagFilterControl tagFilterControl = tagFilterPanel.Children.FirstOrDefault(t => (string)((UserControl)t).Tag == item.Key) as TagFilterControl;
                if (tagFilterControl != null)
                {
                    TagFilterViewModel vm = tagFilterControl.DataContext as TagFilterViewModel;
                    vm.Count = item.Value;

                    int filterControlIndex = tagFilterPanel.Children.IndexOf(tagFilterControl);
                    if (filterControlIndex != index)
                    {
                        tagFilterPanel.Children.Move((uint)filterControlIndex, (uint)index);
                    }
                }
                else
                {
                    TagFilterControl newControl = new TagFilterControl
                    {
                        Tag = item.Key,
                        DataContext = new TagFilterViewModel(item.Key) { Count = item.Value }
                    };

                    newControl.FilterChanged += (s, a) =>
                    {
                        this.FilterFeatureTimeline();
                    };

                    tagFilterPanel.Children.Add(newControl);
                }
                index++;
            }
        }

        private void FilterFeatureTimeline()
        {
            IEnumerable<string> checkedTags = tagFilterPanel.Children.Select(c => (TagFilterViewModel)((TagFilterControl)c).DataContext)
                .Where(vm => vm.IsChecked)
                .Select(vm => vm.Tag);

            if (checkedTags.Any())
            {
                foreach (var item in this.tagsListView.Children)
                {
                    item.Visibility = checkedTags.Contains((string)((VideoTrack)item).Tag) ? Visibility.Visible : Visibility.Collapsed;
                }
            }
            else
            {
                foreach (var item in this.tagsListView.Children)
                {
                    item.Visibility = Visibility.Visible;
                }
            }
        }

        private void UpdateDemographics(SimilarFaceMatch item)
        {
            AgeDistribution genderBasedAgeDistribution = null;
            if (string.Compare(item.Face.FaceAttributes.Gender, "male", StringComparison.OrdinalIgnoreCase) == 0)
            {
                this.demographics.OverallMaleCount++;
                genderBasedAgeDistribution = this.demographics.AgeGenderDistribution.MaleDistribution;
            }
            else
            {
                this.demographics.OverallFemaleCount++;
                genderBasedAgeDistribution = this.demographics.AgeGenderDistribution.FemaleDistribution;
            }

            if (item.Face.FaceAttributes.Age < 16)
            {
                genderBasedAgeDistribution.Age0To15++;
            }
            else if (item.Face.FaceAttributes.Age < 20)
            {
                genderBasedAgeDistribution.Age16To19++;
            }
            else if (item.Face.FaceAttributes.Age < 30)
            {
                genderBasedAgeDistribution.Age20s++;
            }
            else if (item.Face.FaceAttributes.Age < 40)
            {
                genderBasedAgeDistribution.Age30s++;
            }
            else if (item.Face.FaceAttributes.Age < 50)
            {
                genderBasedAgeDistribution.Age40s++;
            }
            else
            {
                genderBasedAgeDistribution.Age50sAndOlder++;
            }
        }

        private static string GetDisplayTextForPersonAsync(ImageAnalyzer analyzer, SimilarFaceMatch item)
        {
            // See if we identified this person against a trained model
            IdentifiedPerson identifiedPerson = analyzer.IdentifiedPersons.FirstOrDefault(p => p.FaceId == item.Face.FaceId);

            if (identifiedPerson != null)
            {
                return identifiedPerson.Person.Name;
            }

            if (identifiedPerson == null)
            {
                // Let's see if this is a celebrity
                if (analyzer.AnalysisResult?.Categories != null)
                {
                    foreach (var category in analyzer.AnalysisResult.Categories.Where(c => c.Detail != null))
                    {
                        dynamic detail = JObject.Parse(category.Detail.ToString());
                        if (detail.celebrities != null)
                        {
                            foreach (var celebrity in detail.celebrities)
                            {
                                uint left = UInt32.Parse(celebrity.faceRectangle.left.ToString());
                                uint top = UInt32.Parse(celebrity.faceRectangle.top.ToString());
                                uint height = UInt32.Parse(celebrity.faceRectangle.height.ToString());
                                uint width = UInt32.Parse(celebrity.faceRectangle.width.ToString());

                                if (Util.AreFacesPotentiallyTheSame(new BitmapBounds { Height = height, Width = width, X = left, Y = top }, item.Face.FaceRectangle))
                                {
                                    return celebrity.name.ToString();
                                }
                            }
                        }
                    }
                }
            }

            return string.Empty;
        }

        private async Task ResetStateAsync()
        {
            this.peopleListView.Children.Clear();
            this.peopleInVideo.Clear();
            await FaceListManager.ResetFaceLists();

            this.demographics = new DemographicsData
            {
                AgeGenderDistribution = new AgeGenderDistribution { FemaleDistribution = new AgeDistribution(), MaleDistribution = new AgeDistribution() },
                Visitors = new List<Visitor>()
            };

            this.tagsInVideo.Clear();
            this.tagFilterPanel.Children.Clear();
            this.tagsListView.Children.Clear();

            this.pendingIdentificationAttemptCount.Clear();
            this.processedFrames.Clear();
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            this.EnterKioskMode();

            if (string.IsNullOrEmpty(SettingsHelper.Instance.EmotionApiKey) ||
                string.IsNullOrEmpty(SettingsHelper.Instance.FaceApiKey) ||
                string.IsNullOrEmpty(SettingsHelper.Instance.VisionApiKey))
            {
                await new MessageDialog("Missing Face, Emotion or Vision API Key. Please enter a key in the Settings page.", "Missing API Key").ShowAsync();
            }

            FaceListManager.FaceListsUserDataFilter = SettingsHelper.Instance.WorkspaceKey + "_RealTimeFromVideo";
            await FaceListManager.Initialize();

            base.OnNavigatedTo(e);
        }

        private void EnterKioskMode()
        {
            ApplicationView view = ApplicationView.GetForCurrentView();
            if (!view.IsFullScreenMode)
            {
                view.TryEnterFullScreenMode();
            }
        }

        private async void FromFileClick(object sender, RoutedEventArgs e)
        {
            FileOpenPicker fileOpenPicker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.Downloads, ViewMode = PickerViewMode.Thumbnail };
            fileOpenPicker.FileTypeFilter.Add(".mp4");

            var pickedFile = await fileOpenPicker.PickSingleFileAsync();
            if (pickedFile != null)
            {

                //Set the stream source to the MediaElement control
                await this.StartVideoAsync(pickedFile);
                StartProcessingLoop();
            }
        }

        private async Task StartVideoAsync(StorageFile file)
        {
            try
            {
                if (this.videoPlayer.CurrentState == MediaElementState.Playing)
                {
                    this.videoPlayer.Stop();
                }

                FrameRelayVideoEffect.ResetState();

                this.currentVideoId = Guid.NewGuid();
                await this.ResetStateAsync();

                MediaClip clip = await MediaClip.CreateFromFileAsync(file);
                clip.VideoEffectDefinitions.Add(new VideoEffectDefinition(typeof(FrameRelayVideoEffect).FullName));

                MediaComposition compositor = new MediaComposition();
                compositor.Clips.Add(clip);

                this.videoPlayer.SetMediaStreamSource(compositor.GenerateMediaStreamSource());
            }
            catch (Exception ex)
            {
                await Util.GenericApiCallExceptionHandler(ex, "Error starting playback.");
            }
        }

        private void TimelineTapped(object sender, Windows.UI.Xaml.Input.TappedRoutedEventArgs e)
        {
            if (this.videoPlayer.CurrentState == MediaElementState.Playing || this.videoPlayer.CurrentState == MediaElementState.Paused)
            {
                var position = e.GetPosition(this.timelineRectangle);
                if (position.X > 0)
                {
                    this.videoPlayer.Position = TimeSpan.FromSeconds((position.X / this.timelineRectangle.ActualWidth) * this.videoPlayer.NaturalDuration.TimeSpan.TotalSeconds);
                }
            }
        }

        private void TagFilterChanged(object sender, RoutedEventArgs e)
        {

        }
    }

    public class DurationToStringConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            return ((Duration)value).TimeSpan.ToString("h\\:mm\\:ss");
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            return new Duration(TimeSpan.Parse(value.ToString()));
        }
    }

    public class TagFilterViewModel : INotifyPropertyChanged
    {
        public bool IsChecked { get; set; }
        public string Tag { get; set; }

        private int count;
        public int Count
        {
            get { return this.count; }
            set
            {
                this.count = value;
                this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Count"));
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public TagFilterViewModel(string tag)
        {
            this.Tag = tag;
        }
    }
}