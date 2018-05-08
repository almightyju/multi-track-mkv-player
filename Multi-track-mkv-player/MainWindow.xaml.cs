using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using Vlc.DotNet.Core;
using Vlc.DotNet.Wpf;
using IO = System.IO;

namespace Multi_track_mkv_player {
    public class VideoVM : INotifyPropertyChanged {
        public event PropertyChangedEventHandler PropertyChanged;
        
        private List<VlcVideoSourceProvider> _videoSources = new List<VlcVideoSourceProvider>();

        public IReadOnlyList<VlcVideoSourceProvider> VideoSources { get => _videoSources; }

        private VlcVideoSourceProvider _mainSource;        
        public VlcVideoSourceProvider MainSource {
            get => _mainSource;
            set {
                _mainSource = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(OtherSources)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(MainSource)));
            }
        }

        public IEnumerable<VlcVideoSourceProvider> OtherSources { get => _videoSources.Where(i => i != _mainSource); }

        private string _timeIndex;
        public string TimeIndex {
            get => _timeIndex;
            set {
                _timeIndex = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TimeIndex)));
            }
        }


        public void AddVideoSource(VlcVideoSourceProvider src) {
            _videoSources.Add(src);
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(OtherSources)));
        }

        public void ClearVideoSources() {
            var sources = _videoSources.ToArray();
            _videoSources.Clear();
            foreach (var s in sources)
                s.Dispose();
        }

        public ICommand SetMainVideoCommand { get; private set; }

        public VideoVM() {
            SetMainVideoCommand = new CommandHandler<VlcVideoSourceProvider>(OnSetMainVideo);
        }

        private void OnSetMainVideo(VlcVideoSourceProvider a) {
            MainSource = a;
        }
    }

    public class CommandHandler<Targ> : ICommand {
        private Action<Targ> _handler;

        public event EventHandler CanExecuteChanged;

        public bool CanExecute(object parameter) {
            return true;
        }

        public void Execute(object parameter) {
            _handler?.Invoke((Targ)parameter);
        }

        public CommandHandler(Action<Targ> handler) {
            _handler = handler;
        }
    }

    public partial class MainWindow : Window {
        private IO.DirectoryInfo _vlcLibDir;
        private string _medialUrl;
        private string _lastFile;
        private bool _pauseOnPlay;

        public VideoVM VM { get; } = new VideoVM();

        public MainWindow() {
            DataContext = VM;

            InitializeComponent();

            _vlcLibDir = new IO.DirectoryInfo(IO.Path.Combine(Environment.CurrentDirectory, "libvlc"));
        }
        
        private void Window_Drop(object sender, DragEventArgs e) {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop))
                return;

            string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (files.Length != 1)
                return;

            IO.FileInfo finfo = new IO.FileInfo(files[0]);
            if (finfo.Extension != ".mkv")
                return;

            OpenFile(finfo.FullName);
        }

        private void OpenFile(string path) {
            _lastFile = path;
            _medialUrl = $"file:///{path.Replace('\\', '/')}";

            VM.ClearVideoSources();

            var mainSrc = new VlcVideoSourceProvider(Dispatcher);
            mainSrc.CreatePlayer(_vlcLibDir);
            mainSrc.MediaPlayer.SetMedia(_medialUrl);

            mainSrc.MediaPlayer.Playing += MediaPlayer_Playing;
            mainSrc.MediaPlayer.PositionChanged += MediaPlayer_PositionChanged;
            mainSrc.MediaPlayer.TimeChanged += MediaPlayer_TimeChanged;
            mainSrc.MediaPlayer.EndReached += MediaPlayer_EndReached;

            mainSrc.MediaPlayer.Play();

            VM.AddVideoSource(mainSrc);
            VM.MainSource = mainSrc;
        }

        private void MediaPlayer_EndReached(object sender, VlcMediaPlayerEndReachedEventArgs e) {
            Dispatcher.BeginInvoke(new Action(() => {
                _pauseOnPlay = true;
                OpenFile(_lastFile);                
            }));
        }

        private void MediaPlayer_TimeChanged(object sender, VlcMediaPlayerTimeChangedEventArgs e) {
            long time = e.NewTime / 1000;
            VM.TimeIndex = $"{Math.Floor((decimal)time / 60).ToString().PadLeft(2, '0')}:{(time % 60).ToString().PadLeft(2, '0')}";
        }

        private void MediaPlayer_Playing(object sender, Vlc.DotNet.Core.VlcMediaPlayerPlayingEventArgs e) {
            //1 is -1 ("disable") and second is whats in the main player
            for (int i = 2; i < VM.MainSource.MediaPlayer.Video.Tracks.Count; i++) {
                VlcVideoSourceProvider ctl = CreatePlayer(i);
                VM.AddVideoSource(ctl);
                ctl.MediaPlayer.Play();
            }

            _ignoreVolumeValChange = true;
            Dispatcher.Invoke(() => volumeSlider.Value = VM.MainSource.MediaPlayer.Audio.Volume);
            _ignoreVolumeValChange = false;

            if (_pauseOnPlay)
                Dispatcher.BeginInvoke(new Action(() => {
                    Pause(this, null);
                    timelineSlider.Value = 0;
                }));
            _pauseOnPlay = false;

            VM.MainSource.MediaPlayer.Playing -= MediaPlayer_Playing;
        }

        private bool _ignoreTimelineValChange = false;
        private bool _isUserDragTimeline = false;
        private bool _ignoreVolumeValChange = false;
        private void MediaPlayer_PositionChanged(object sender, VlcMediaPlayerPositionChangedEventArgs e) {
            if (_isUserDragTimeline) return;
            
            Dispatcher.BeginInvoke(new Action(() => {
                _ignoreTimelineValChange = true;
                timelineSlider.Value = e.NewPosition;
                _ignoreTimelineValChange = false;
            }));
        }

        private VlcVideoSourceProvider CreatePlayer(int trackIdx) {
            VlcVideoSourceProvider ctl = new VlcVideoSourceProvider(Dispatcher);
            ctl.CreatePlayer(_vlcLibDir);
            ctl.MediaPlayer.SetMedia(_medialUrl);
            EventHandler<VlcMediaPlayerPlayingEventArgs> playingCb = null;
            playingCb = (s, e2) => {
                ctl.MediaPlayer.Video.Tracks.Current = ctl.MediaPlayer.Video.Tracks.All.ElementAt(trackIdx);
                if(ctl.MediaPlayer.Audio.Tracks.Count > 0)
                    ctl.MediaPlayer.Audio.Tracks.Current = ctl.MediaPlayer.Audio.Tracks.All.First(); //disable
                ctl.MediaPlayer.Playing -= playingCb;
            };
            ctl.MediaPlayer.Playing += playingCb;

            return ctl;
        }


        private void Play(object sender, RoutedEventArgs e) {
            foreach (var s in VM.VideoSources)
                s.MediaPlayer.Play();
        }

        private void Pause(object sender, RoutedEventArgs e) {
            foreach (var s in VM.VideoSources)
                s.MediaPlayer.Pause();
        }

        private void timelineSlider_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e) {            
            _isUserDragTimeline = false;
        }

        private void timelineSlider_DragStarted(object sender, System.Windows.Controls.Primitives.DragStartedEventArgs e) {
            _isUserDragTimeline = true;
        }

        private void timelineSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) {
            if (_ignoreTimelineValChange)
                return;

            foreach (var s in VM.VideoSources)
                s.MediaPlayer.Position = (float)timelineSlider.Value;
        }

        private void volumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) {
            if(!_ignoreVolumeValChange)
                VM.MainSource.MediaPlayer.Audio.Volume = (int)e.NewValue;
        }

        private void Menu_Open(object sender, RoutedEventArgs e) {
            OpenFileDialog d = new OpenFileDialog();
            d.Title = "Choose MKV File";
            d.Filter = "MKV Files (*.mkv)|*.mkv";
            if (d.ShowDialog() == true)
                OpenFile(d.FileName);
        }

        private void Menu_Exit(object sender, RoutedEventArgs e) {
            Application.Current.Shutdown();
        }
    }
}
