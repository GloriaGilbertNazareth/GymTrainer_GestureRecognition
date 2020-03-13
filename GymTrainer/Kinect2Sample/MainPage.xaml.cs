using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Navigation;
using WindowsPreview.Kinect;
using System.ComponentModel;
using Windows.Storage.Streams;
using System.Runtime.InteropServices;
using System.Diagnostics;
using Windows.UI.Xaml.Shapes;
using Windows.UI;
using KinectFace;
using Microsoft.Kinect.Face;
using Windows.Storage.Pickers;
using Windows.Graphics.Imaging;
using Windows.Graphics.Display;
using Windows.Storage;


namespace Kinect2Sample
{
    public enum DisplayFrameType
    {
        BodyJoints,
        None
    }

    public sealed partial class MainPage : Page, INotifyPropertyChanged
    {
        private const DisplayFrameType DEFAULT_DISPLAYFRAMETYPE = DisplayFrameType.None;

        // Size of the RGB pixel in the bitmap
        private const int BytesPerPixel = 4;

        private KinectSensor kinectSensor = null;
        private string statusText = null;
        private WriteableBitmap bitmap = null;
        private FrameDescription currentFrameDescription;
        private DisplayFrameType currentDisplayFrameType;
        private MultiSourceFrameReader multiSourceFrameReader = null;
        private CoordinateMapper coordinateMapper = null;
        private BodiesManager bodiesManager = null;
        private string exercise = null;
        private int count=0;
        private DateTime startTime = DateTime.Now;
        private DateTime endTime = DateTime.Now;

        //Infrared Frame 
        private ushort[] infraredFrameData = null;
        private byte[] infraredPixels = null;

        //Depth Frame
        private ushort[] depthFrameData = null;
        private byte[] depthPixels = null;
        private ushort depthMax = 8000;

        //BodyMask Frames
        private DepthSpacePoint[] colorMappedToDepthPoints = null;

        //Body Joints are drawn here
        private Canvas drawingCanvas;

        //FaceManager library
        private FaceManager faceManager;
        private FaceFrameFeatures faceFrameFeatures;

        /// <summary> List of gesture detectors, there will be one detector created for each potential body (max of 6) </summary>
        private List<GestureDetector> gestureDetectorList = null;
        public bool isTakingScreenshot = false;

        public event PropertyChangedEventHandler PropertyChanged;

        public string StatusText
        {
            get { return this.statusText; }
            set
            {
                if (this.statusText != value)
                {
                    this.statusText = value;
                    if (this.PropertyChanged != null)
                    {
                        this.PropertyChanged(this, new PropertyChangedEventArgs("StatusText"));
                    }
                }
            }
        }

        public FrameDescription CurrentFrameDescription
        {
            get { return this.currentFrameDescription; }
            set
            {
                if (this.currentFrameDescription != value)
                {
                    this.currentFrameDescription = value;
                    if (this.PropertyChanged != null)
                    {
                        this.PropertyChanged(this, new PropertyChangedEventArgs("CurrentFrameDescription"));
                    }
                }
            }
        }

        public DisplayFrameType CurrentDisplayFrameType
        {
            get { return this.currentDisplayFrameType; }
            set
            {
                if (this.currentDisplayFrameType != value)
                {
                    this.currentDisplayFrameType = value;
                    if (this.PropertyChanged != null)
                    {
                        this.PropertyChanged(this, new PropertyChangedEventArgs("CurrentDisplayFrameType"));
                    }
                }
            }
        }

        public MainPage()
        {
            // one sensor is currently supported
            this.kinectSensor = KinectSensor.GetDefault();

            this.coordinateMapper = this.kinectSensor.CoordinateMapper;

            this.multiSourceFrameReader = this.kinectSensor.OpenMultiSourceFrameReader(FrameSourceTypes.Infrared | FrameSourceTypes.Color | FrameSourceTypes.Depth | FrameSourceTypes.BodyIndex | FrameSourceTypes.Body);

            this.multiSourceFrameReader.MultiSourceFrameArrived += this.Reader_MultiSourceFrameArrived;

            this.faceManager = new FaceManager(this.kinectSensor, this.faceFrameFeatures);

            // set IsAvailableChanged event notifier
            this.kinectSensor.IsAvailableChanged += this.Sensor_IsAvailableChanged;

            // use the window object as the view model in this simple example
            this.DataContext = this;

            // open the sensor
            this.kinectSensor.Open();

            this.InitializeComponent();

            this.Loaded += MainPage_Loaded;

            this.gestureDetectorList = new List<GestureDetector>();
            Gesture_Loaded("");
           
        }

        void Gesture_Loaded(string exercise)
        {
            //lab 13
            // Initialize the gesture detection objects for our gestures
            this.gestureDetectorList = new List<GestureDetector>();

            //lab 13
            // Create a gesture detector for each body (6 bodies => 6 detectors)
            int maxBodies = this.kinectSensor.BodyFrameSource.BodyCount;
            for (int i = 0; i < maxBodies; ++i)
            {
                GestureResultView result = new GestureResultView(i, false, false, 0.0f);
                GestureDetector detector = new GestureDetector(this.kinectSensor, result, exercise);
                result.PropertyChanged += GestureResult_PropertyChanged;
                this.gestureDetectorList.Add(detector);
            }
        }

        void MainPage_Loaded(object sender, RoutedEventArgs e)
        {
            SetupCurrentDisplay(DEFAULT_DISPLAYFRAMETYPE, false);

        }


        private void SetupCurrentDisplay(DisplayFrameType newDisplayFrameType, bool isFullScreen = true)
        {
            if (isFullScreen)
            {
                RootGrid.RowDefinitions.Clear();
                RootGrid.RowDefinitions.Add(new RowDefinition() { Height = new GridLength(0) });
                RootGrid.RowDefinitions.Add(new RowDefinition() { Height = new GridLength(1, GridUnitType.Star) });
                RootGrid.RowDefinitions.Add(new RowDefinition() { Height = new GridLength(0) });
                FullScreenBackButton.Visibility = Windows.UI.Xaml.Visibility.Visible;
            }
            else
            {
                RootGrid.RowDefinitions.Clear();
                RootGrid.RowDefinitions.Add(new RowDefinition() { Height = new GridLength(70) });
                RootGrid.RowDefinitions.Add(new RowDefinition() { Height = new GridLength(1, GridUnitType.Star) });
                RootGrid.RowDefinitions.Add(new RowDefinition() { Height = new GridLength(100) });
                FullScreenBackButton.Visibility = Windows.UI.Xaml.Visibility.Collapsed;
            }

            CurrentDisplayFrameType = newDisplayFrameType;
            // Frames used by more than one type are declared outside the switch
            FrameDescription colorFrameDescription = null;
            FrameDescription depthFrameDescription = null;
            FrameDescription infraredFrameDescription = null;
            // reset the display methods
            FacePointsCanvas.Children.Clear();
            if (this.BodyJointsGrid != null)
            {
                this.BodyJointsGrid.Visibility = Visibility.Collapsed;
            }
            if (this.FrameDisplayImage != null)
            {
                this.FrameDisplayImage.Source = null;
            }
            switch (CurrentDisplayFrameType)
            {
                
                case DisplayFrameType.BodyJoints:
                    depthFrameDescription = this.kinectSensor.DepthFrameSource.FrameDescription;
                    // instantiate a new Canvas
                    this.drawingCanvas = new Canvas();
                    // set the clip rectangle to prevent rendering outside the canvas
                    this.drawingCanvas.Clip = new RectangleGeometry();
                    this.drawingCanvas.Clip.Rect = new Rect(0.0, 0.0, this.BodyJointsGrid.Width, this.BodyJointsGrid.Height);
                    this.drawingCanvas.Width = this.BodyJointsGrid.Width;
                    this.drawingCanvas.Height = this.BodyJointsGrid.Height;
                    // reset the body joints grid
                    this.BodyJointsGrid.Visibility = Visibility.Visible;
                    this.BodyJointsGrid.Children.Clear();
                    // add canvas to DisplayGrid
                    this.BodyJointsGrid.Children.Add(this.drawingCanvas);
                    bodiesManager = new BodiesManager(this.coordinateMapper, this.drawingCanvas, this.kinectSensor.BodyFrameSource.BodyCount);
                    break;

                
                default:
                    break;
            }
        }

        private void Sensor_IsAvailableChanged(KinectSensor sender, IsAvailableChangedEventArgs args)
        {
            this.StatusText = this.kinectSensor.IsAvailable ? "Running" : "Not Available";
        }

        private void Reader_MultiSourceFrameArrived(MultiSourceFrameReader sender, MultiSourceFrameArrivedEventArgs e)
        {
            MultiSourceFrame multiSourceFrame = e.FrameReference.AcquireFrame();

            // If the Frame has expired by the time we process this event, return.
            if (multiSourceFrame == null)
            {
                return;
            }
            DepthFrame depthFrame = null;
            ColorFrame colorFrame = null;
            InfraredFrame infraredFrame = null;
            BodyFrame bodyFrame = null;
            BodyIndexFrame bodyIndexFrame = null;
            IBuffer depthFrameDataBuffer = null;
            IBuffer bodyIndexFrameData = null;
            // Com interface for unsafe byte manipulation
            IBufferByteAccess bufferByteAccess = null;

            //lab 13
            using (bodyFrame = multiSourceFrame.BodyFrameReference.AcquireFrame())
            {
                RegisterGesture(bodyFrame);
            }


            switch (CurrentDisplayFrameType)
            {
                
                case DisplayFrameType.BodyJoints:
                    using (bodyFrame = multiSourceFrame.BodyFrameReference.AcquireFrame())
                    {
                        ShowBodyJoints(bodyFrame);
                    }
                    break;
               default:
                    break;
            }
        }

        //lab 13
        void GestureResult_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            GestureResultView result = sender as GestureResultView;
            this.GestureVisual.Text = "Performing "+this.exercise+" with confidence level: "+(Math.Round(result.Confidence, 2)).ToString()+" Count: "+this.count;
            if (result.Confidence ==1 )
            {
                this.endTime = DateTime.Now;
                if((this.endTime - this.startTime).TotalSeconds>0.5)
                    this.count++;
                this.startTime = DateTime.Now;
            }
        }

        
        private void ShowBodyJoints(BodyFrame bodyFrame)
        {
            Body[] bodies = new Body[this.kinectSensor.BodyFrameSource.BodyCount];
            bool dataReceived = false;
            if (bodyFrame != null)
            {
                bodyFrame.GetAndRefreshBodyData(bodies);
                dataReceived = true;
            }

            if (dataReceived)
            {
                this.bodiesManager.UpdateBodiesAndEdges(bodies);
            }
        }

        

        //lab 13
        private void RegisterGesture(BodyFrame bodyFrame)
        {
            bool dataReceived = false;
            Body[] bodies = null;

            if (bodyFrame != null)
            {
                if (bodies == null)
                {
                    // Creates an array of 6 bodies, which is the max number of bodies that Kinect can track simultaneously
                    bodies = new Body[bodyFrame.BodyCount];
                }

                // The first time GetAndRefreshBodyData is called, Kinect will allocate each Body in the array.
                // As long as those body objects are not disposed and not set to null in the array,
                // those body objects will be re-used.
                bodyFrame.GetAndRefreshBodyData(bodies);
                dataReceived = true;
            }

            if (dataReceived)
            {
                // We may have lost/acquired bodies, so update the corresponding gesture detectors
                if (bodies != null)
                {
                    // Loop through all bodies to see if any of the gesture detectors need to be updated
                    for (int i = 0; i < bodyFrame.BodyCount; ++i)
                    {
                        Body body = bodies[i];
                        ulong trackingId = body.TrackingId;

                        // If the current body TrackingId changed, update the corresponding gesture detector with the new value
                        if (trackingId != this.gestureDetectorList[i].TrackingId)
                        {
                            this.gestureDetectorList[i].TrackingId = trackingId;

                            // If the current body is tracked, unpause its detector to get VisualGestureBuilderFrameArrived events
                            // If the current body is not tracked, pause its detector so we don't waste resources trying to get invalid gesture results
                            this.gestureDetectorList[i].IsPaused = trackingId == 0;
                        }
                    }
                }
            }
        }

        

        private void RenderPixelArray(byte[] pixels)
        {
            pixels.CopyTo(this.bitmap.PixelBuffer);
            this.bitmap.Invalidate();
            this.FrameDisplayImage.Source = this.bitmap;
        }


        private void Squats_Click(object sender, RoutedEventArgs e)
        {
            this.exercise = "Squats";
            this.count = 0;
            Gesture_Loaded(exercise);
            SetupCurrentDisplay(DisplayFrameType.BodyJoints);
        }
        private void Kettlebells_Click(object sender, RoutedEventArgs e)
        {
            this.exercise = "kettlebells";
            Gesture_Loaded(exercise);
            this.count = 0;
            SetupCurrentDisplay(DisplayFrameType.BodyJoints);
        }
        private void Chestpress_Click(object sender, RoutedEventArgs e)
        {
            this.exercise = "chestpress";
            Gesture_Loaded(exercise);
            this.count = 0;
            SetupCurrentDisplay(DisplayFrameType.BodyJoints);
        }
        private void Jumpingjacks_Click(object sender, RoutedEventArgs e)
        {
            this.exercise = "jumpingjacks";
            this.count = 0;
            Gesture_Loaded(exercise);
            SetupCurrentDisplay(DisplayFrameType.BodyJoints);
        }

        private void FullScreenBackButton_Click(object sender, RoutedEventArgs e)
        {
            SetupCurrentDisplay(CurrentDisplayFrameType, false);
        }

        [Guid("905a0fef-bc53-11df-8c49-001e4fc686da"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IBufferByteAccess
        {
            unsafe void Buffer(out byte* pByte);
        }
    }
}
