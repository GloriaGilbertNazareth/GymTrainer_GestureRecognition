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
    {   Infrared,
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
        //Infrared Frame
        private ushort[] infraredFrameData = null;
        private byte[] infraredPixels = null;
        private FrameDescription currentFrameDescription;
        private DisplayFrameType currentDisplayFrameType;
        private MultiSourceFrameReader multiSourceFrameReader = null;
        private CoordinateMapper coordinateMapper = null;
        private BodiesManager bodiesManager = null;
        private string exercise = null;
        private int count=0;
        private DateTime startTime = DateTime.Now;
        private DateTime endTime = DateTime.Now;
        private DateTime exstartTime = DateTime.Now;

        /// <summary>
    /// The highest value that can be returned in the InfraredFrame.
    /// It is cast to a float for readability in the visualization code.
    /// </summary>
    private const float InfraredSourceValueMaximum = 
        (float)ushort.MaxValue;

    /// </summary>
    /// Used to set the lower limit, post processing, of the
    /// infrared data that we will render.
    /// Increasing or decreasing this value sets a brightness
    /// "wall" either closer or further away.
    /// </summary>
    private const float InfraredOutputValueMinimum = 0.01f;
    
    /// <summary>
    /// The upper limit, post processing, of the
    /// infrared data that will render.
    /// </summary>
    private const float InfraredOutputValueMaximum = 1.0f;

    /// <summary>
    /// The InfraredSceneValueAverage value specifies the 
    /// average infrared value of the scene. 
    /// This value was selected by analyzing the average
    /// pixel intensity for a given scene.
    /// This could be calculated at runtime to handle different
    /// IR conditions of a scene (outside vs inside).
    /// </summary>
    private const float InfraredSceneValueAverage = 0.08f;
	
    /// <summary>
    /// The InfraredSceneStandardDeviations value specifies 
    /// the number of standard deviations to apply to
    /// InfraredSceneValueAverage.
    /// This value was selected by analyzing data from a given scene.
    /// This could be calculated at runtime to handle different
    /// IR conditions of a scene (outside vs inside).
    /// </summary>
    private const float InfraredSceneStandardDeviations = 3.0f;

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
        private void ShowInfraredFrame(InfraredFrame infraredFrame)
    {
        bool infraredFrameProcessed = false;

            if (infraredFrame != null)
            {
                FrameDescription infraredFrameDescription = 
			infraredFrame.FrameDescription;

                // verify data and write the new infrared frame data
                // to the display bitmap
                if (((infraredFrameDescription.Width * 
                    infraredFrameDescription.Height) 
			     == this.infraredFrameData.Length) &&
                    (infraredFrameDescription.Width == 
                    this.bitmap.PixelWidth) && 
			(infraredFrameDescription.Height == 
                this.bitmap.PixelHeight))
                {
                    // Copy the pixel data from the image to a 
                    // temporary array
                    infraredFrame.CopyFrameDataToArray(
                        this.infraredFrameData);

                    infraredFrameProcessed = true;
                }
            
        }     // we got a frame, convert and render
        if (infraredFrameProcessed)
        {
            ConvertInfraredDataToPixels();
            RenderPixelArray (this.infraredPixels);
        }
    }

        // Reader_InfraredFrameArrived() before this...
        private void ConvertInfraredDataToPixels()
        {
            // Convert the infrared to RGB
            int colorPixelIndex = 0;
            for (int i = 0; i < this.infraredFrameData.Length; ++i)
            {
                // normalize the incoming infrared data (ushort) to 
                // a float ranging from InfraredOutputValueMinimum
                // to InfraredOutputValueMaximum] by

                // 1. dividing the incoming value by the 
                // source maximum value
                float intensityRatio = (float)this.infraredFrameData[i] /
            InfraredSourceValueMaximum;

                // 2. dividing by the 
                // (average scene value * standard deviations)
                intensityRatio /=
                 InfraredSceneValueAverage * InfraredSceneStandardDeviations;

                // 3. limiting the value to InfraredOutputValueMaximum
                intensityRatio = Math.Min(InfraredOutputValueMaximum,
                    intensityRatio);

                // 4. limiting the lower value InfraredOutputValueMinimum
                intensityRatio = Math.Max(InfraredOutputValueMinimum,
                    intensityRatio);

                // 5. converting the normalized value to a byte and using 
                // the result as the RGB components required by the image
                byte intensity = (byte)(intensityRatio * 255.0f);
                this.infraredPixels[colorPixelIndex++] = intensity; //Blue
                this.infraredPixels[colorPixelIndex++] = intensity; //Green
                this.infraredPixels[colorPixelIndex++] = intensity; //Red
                this.infraredPixels[colorPixelIndex++] = 255;       //Alpha           
            }
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
                case DisplayFrameType.Infrared:
                    FrameDescription infraredFrameDescription =
                         this.kinectSensor.InfraredFrameSource.FrameDescription;
                    this.CurrentFrameDescription = infraredFrameDescription;
                    // allocate space to put the pixels being 
                    // received and converted
                    this.infraredFrameData =
                        new ushort[infraredFrameDescription.Width *
                         infraredFrameDescription.Height];
                    this.infraredPixels =
                        new byte[infraredFrameDescription.Width *
                         infraredFrameDescription.Height * BytesPerPixel];
                    this.bitmap =
                        new WriteableBitmap(infraredFrameDescription.Width,
                         infraredFrameDescription.Height);
                    break;
                
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
                case DisplayFrameType.Infrared:
                    using (InfraredFrame infraredFrame =
                multiSourceFrame.InfraredFrameReference.AcquireFrame())
                    {
                        ShowInfraredFrame(infraredFrame);
                    }
                    break;
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
            this.GestureVisual.Text = "Confidence level: " + (Math.Round(result.Confidence, 2)).ToString() + " Count: " + this.count + " Time: " + (DateTime.Now - this.exstartTime).ToString();
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
            this.exstartTime = DateTime.Now;

            this.squatsImg.Visibility = Windows.UI.Xaml.Visibility.Visible;
            this.kettlebellsImg.Visibility = Windows.UI.Xaml.Visibility.Collapsed;
            this.chestpressImg.Visibility = Windows.UI.Xaml.Visibility.Collapsed;
            this.jumpingjacksImg.Visibility = Windows.UI.Xaml.Visibility.Collapsed;
            this.armraiseImg.Visibility = Windows.UI.Xaml.Visibility.Collapsed;
            this.dumbbellImg.Visibility = Windows.UI.Xaml.Visibility.Collapsed;
            this.Instruction.Text = "Follow the below instructions:";
            this.Instruction1.Text = "1. Stand with your head facing forward, your chest held up and hands stretching outwards.";
            this.Instruction2.Text = "2. Place your feet shoulder-width apart or slightly wider";
            this.Instruction3.Text = "3. Sit back and down with your thighs as parallel to the floor as possible";
            this.count = 0;
            Gesture_Loaded(exercise);
            SolidColorBrush silverBrush = new SolidColorBrush(Windows.UI.Colors.Silver);
            this.Squats.Background = silverBrush;
            SolidColorBrush blackBrush = new SolidColorBrush(Windows.UI.Colors.Black);
            this.Kettlebells.Background = blackBrush;          
            this.Chestpress.Background = blackBrush;
            this.Jumpingjacks.Background = blackBrush;
            this.Armraises.Background = blackBrush;
            this.Dumbbells.Background = blackBrush;


            SetupCurrentDisplay(DisplayFrameType.Infrared, false);
        }
        private void Kettlebells_Click(object sender, RoutedEventArgs e)
        {
            this.exercise = "kettlebells";
            this.exstartTime = DateTime.Now;

            this.Instruction.Text = "Follow the below instructions:";
            this.Instruction1.Text = "1. Stand behind kettlebell with feet slightly wider apart than shoulder width.";
            this.Instruction2.Text = "2. Drive hips forward, and knees straight so kettlebell is pushed forward and upward.";
            this.Instruction3.Text = "3. Swing kettlebell back down between legs.";

            this.squatsImg.Visibility = Windows.UI.Xaml.Visibility.Collapsed;
            this.kettlebellsImg.Visibility = Windows.UI.Xaml.Visibility.Visible;
            this.chestpressImg.Visibility = Windows.UI.Xaml.Visibility.Collapsed;
            this.jumpingjacksImg.Visibility = Windows.UI.Xaml.Visibility.Collapsed;
            this.armraiseImg.Visibility = Windows.UI.Xaml.Visibility.Collapsed;
            this.dumbbellImg.Visibility = Windows.UI.Xaml.Visibility.Collapsed;
             SolidColorBrush silverBrush = new SolidColorBrush(Windows.UI.Colors.Silver);

            this.Kettlebells.Background = silverBrush;
            SolidColorBrush blackBrush = new SolidColorBrush(Windows.UI.Colors.Black);
            this.Squats.Background = blackBrush;          
            this.Chestpress.Background = blackBrush;
            this.Jumpingjacks.Background = blackBrush;
            this.Armraises.Background = blackBrush;
            this.Dumbbells.Background = blackBrush;
            Gesture_Loaded(exercise);
            this.count = 0;
            SetupCurrentDisplay(DisplayFrameType.Infrared, false);
        }
        private void Chestpress_Click(object sender, RoutedEventArgs e)
        {
            this.exercise = "chestpress";
            this.exstartTime = DateTime.Now;

            this.Instruction.Text = "Follow the below instructions:";
            this.Instruction1.Text = "1. Hold a dumbbell in each hand and sit on a bench with feet firm on the floor";
            this.Instruction2.Text = "2. Bend your elbows, raise your upper arms to shoulder height so the dumbbells are at ear lvl.";
            this.Instruction3.Text = "3. Push the dumbbells up over your head, and then lower the dumbbells back to ear level.";
            this.squatsImg.Visibility = Windows.UI.Xaml.Visibility.Collapsed;
            this.kettlebellsImg.Visibility = Windows.UI.Xaml.Visibility.Collapsed;
            this.chestpressImg.Visibility = Windows.UI.Xaml.Visibility.Visible;
            this.jumpingjacksImg.Visibility = Windows.UI.Xaml.Visibility.Collapsed;
            this.armraiseImg.Visibility = Windows.UI.Xaml.Visibility.Collapsed;
            this.dumbbellImg.Visibility = Windows.UI.Xaml.Visibility.Collapsed;
            SolidColorBrush silverBrush = new SolidColorBrush(Windows.UI.Colors.Silver);

            this.Chestpress.Background = silverBrush;
            SolidColorBrush blackBrush = new SolidColorBrush(Windows.UI.Colors.Black);
            this.Squats.Background = blackBrush;
            this.Kettlebells.Background = blackBrush;
            this.Jumpingjacks.Background = blackBrush;
            this.Armraises.Background = blackBrush;
            this.Dumbbells.Background = blackBrush;
            Gesture_Loaded(exercise);
            this.count = 0;
            SetupCurrentDisplay(DisplayFrameType.Infrared, false);
        }
        private void Jumpingjacks_Click(object sender, RoutedEventArgs e)
        {
            this.exercise = "jumpingjacks";
            this.exstartTime = DateTime.Now;
            this.Instruction.Text = "Follow the below instructions:";
            this.Instruction1.Text = "1. Stand upright with your legs together, arms at your sides.";
            this.Instruction2.Text = "2. Bend your knees slightly, and jump into the air.";
            this.Instruction3.Text = "3. As you jump, spread your legs to be about shoulder-width apart and arms above your head.";
            this.squatsImg.Visibility = Windows.UI.Xaml.Visibility.Collapsed;
            this.kettlebellsImg.Visibility = Windows.UI.Xaml.Visibility.Collapsed;
            this.chestpressImg.Visibility = Windows.UI.Xaml.Visibility.Collapsed;
            this.jumpingjacksImg.Visibility = Windows.UI.Xaml.Visibility.Visible;
            this.armraiseImg.Visibility = Windows.UI.Xaml.Visibility.Collapsed;
            this.dumbbellImg.Visibility = Windows.UI.Xaml.Visibility.Collapsed;
            
            SolidColorBrush silverBrush = new SolidColorBrush(Windows.UI.Colors.Silver);

            this.Jumpingjacks.Background = silverBrush;
            SolidColorBrush blackBrush = new SolidColorBrush(Windows.UI.Colors.Black);
            this.Squats.Background = blackBrush;
            this.Kettlebells.Background = blackBrush;
            this.Chestpress.Background = blackBrush;
            this.Armraises.Background = blackBrush;
            this.Dumbbells.Background = blackBrush;

            this.count = 0;
            Gesture_Loaded(exercise);
            SetupCurrentDisplay(DisplayFrameType.Infrared, false);
        }



        private void Armraises_Click(object sender, RoutedEventArgs e)
        {
            this.exercise = "armraises";
            this.exstartTime = DateTime.Now;


            this.Instruction.Text = "Follow the below instructions:";
            this.Instruction1.Text = "1. Stand upright with your legs together, arms at your sides.";
            this.Instruction2.Text = "2. Raise your arms upfront till your shoulder levels.";
            this.Instruction3.Text = "3. Hold your arms in this position or a second and bring them down slowly.";
            this.squatsImg.Visibility = Windows.UI.Xaml.Visibility.Collapsed;
            this.kettlebellsImg.Visibility = Windows.UI.Xaml.Visibility.Collapsed;
            this.chestpressImg.Visibility = Windows.UI.Xaml.Visibility.Collapsed;
            this.jumpingjacksImg.Visibility = Windows.UI.Xaml.Visibility.Collapsed;
            this.armraiseImg.Visibility = Windows.UI.Xaml.Visibility.Visible;
            this.dumbbellImg.Visibility = Windows.UI.Xaml.Visibility.Collapsed;

            SolidColorBrush silverBrush = new SolidColorBrush(Windows.UI.Colors.Silver);

            this.Armraises.Background = silverBrush;
            SolidColorBrush blackBrush = new SolidColorBrush(Windows.UI.Colors.Black);
            this.Squats.Background = blackBrush;
            this.Kettlebells.Background = blackBrush;
            this.Chestpress.Background = blackBrush;
            this.Dumbbells.Background = blackBrush;
            this.Jumpingjacks.Background = blackBrush;

            this.count = 0;
            Gesture_Loaded(exercise);
            SetupCurrentDisplay(DisplayFrameType.Infrared, false);
        }

        private void Dumbbells_Click(object sender, RoutedEventArgs e)
        {
            this.exercise = "dumbbell";
            this.exstartTime = DateTime.Now;

            this.Instruction.Text = "Follow the below instructions:";
            this.Instruction1.Text = "1. Stand upright with your legs together, arms at your sides.";
            this.Instruction2.Text = "2. With the dumbbells in your hands, Curl your armss.";
            this.Instruction3.Text = "3. Hold your arms in this position or a second and bring them down slowly.";
            this.squatsImg.Visibility = Windows.UI.Xaml.Visibility.Collapsed;
            this.kettlebellsImg.Visibility = Windows.UI.Xaml.Visibility.Collapsed;
            this.chestpressImg.Visibility = Windows.UI.Xaml.Visibility.Collapsed;
            this.jumpingjacksImg.Visibility = Windows.UI.Xaml.Visibility.Collapsed;
            this.armraiseImg.Visibility = Windows.UI.Xaml.Visibility.Collapsed;
            this.dumbbellImg.Visibility = Windows.UI.Xaml.Visibility.Visible;

            SolidColorBrush silverBrush = new SolidColorBrush(Windows.UI.Colors.Silver);

            this.Dumbbells.Background = silverBrush;
            SolidColorBrush blackBrush = new SolidColorBrush(Windows.UI.Colors.Black);
            this.Squats.Background = blackBrush;
            this.Kettlebells.Background = blackBrush;
            this.Chestpress.Background = blackBrush;
            this.Jumpingjacks.Background = blackBrush;
            this.Armraises.Background = blackBrush;
            this.count = 0;
            Gesture_Loaded(exercise);
            SetupCurrentDisplay(DisplayFrameType.Infrared, false);
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
