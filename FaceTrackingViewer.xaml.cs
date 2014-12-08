// --------------------------------------------------------------------------------------------------------------------
// <copyright file="FaceTrackingViewer.xaml.cs" company="Microsoft">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
// --------------------------------------------------------------------------------------------------------------------

namespace FaceTrackingBasics
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Windows;
    using System.Windows.Controls;
    using System.Windows.Media;
    using Microsoft.Kinect;
    using Microsoft.Kinect.Toolkit.FaceTracking;
    using System.Windows.Forms;
    using System.Windows.Input;
    using Point = System.Windows.Point;
    using System.Runtime.InteropServices;
    using WindowsInput;
    using System.Threading;
    using Bespoke.Common.Osc;
    using System.Net;

    /// <summary>
    /// Class that uses the Face Tracking SDK to display a face mask for
    /// tracked skeletons
    /// </summary>
    public partial class FaceTrackingViewer : IDisposable
    {
        public static readonly DependencyProperty KinectProperty = DependencyProperty.Register(
            "Kinect",
            typeof(KinectSensor),
            typeof(FaceTrackingViewer),
            new PropertyMetadata(
                null, (o, args) => ((FaceTrackingViewer)o).OnSensorChanged((KinectSensor)args.OldValue, (KinectSensor)args.NewValue)));

        private const uint MaxMissedFrames = 100;

        private readonly Dictionary<int, SkeletonFaceTracker> trackedSkeletons = new Dictionary<int, SkeletonFaceTracker>();

        private byte[] colorImage;

        private ColorImageFormat colorImageFormat = ColorImageFormat.Undefined;

        private short[] depthImage;

        private DepthImageFormat depthImageFormat = DepthImageFormat.Undefined;

        private bool disposed;

        private Skeleton[] skeletonData;

        SkeletonFaceTracker skeletonFaceTracker;

        [DllImport("user32.dll", EntryPoint = "keybd_event", CharSet = CharSet.Auto, ExactSpelling = true)]
        public static extern void Keybd_event(byte vk, byte scan, int flags, int extrainfo);

        [DllImport("user32.dll")]
        private static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, int dwExtraInfo);


        private const int MOUSEEVENTF_LEFTDOWN = 0x02;
        private const int MOUSEEVENTF_LEFTUP = 0x04;
        private const int MOUSEEVENTF_RIGHTDOWN = 0x08;
        private const int MOUSEEVENTF_RIGHTUP = 0x10;

        const int VK_W = 0x57; //w key
        const int VK_A = 0x41;  //s key
        const int VK_S = 0x53;
        const int VK_D = 0x44;
        const int VK_UP = 0x26;
        const int VK_SPACE = 0x20;

        const uint KEYEVENTF_KEYUP = 0x0002;
        const uint KEYEVENTF_EXTENDEDKEY = 0x0001;

        static IPEndPoint myapp;
        static IPEndPoint glovepie;
        static OscMessage msg;
        public FaceTrackingViewer()
        {
            this.InitializeComponent();
            OscPacket.LittleEndianByteOrder = false;
            myapp = new IPEndPoint(IPAddress.Loopback, 1944);
            glovepie = new IPEndPoint(IPAddress.Loopback, 1945);
        }

        ~FaceTrackingViewer()
        {
            this.Dispose(false);
        }

        public KinectSensor Kinect
        {
            get
            {
                return (KinectSensor)this.GetValue(KinectProperty);
            }

            set
            {
                this.SetValue(KinectProperty, value);
            }
        }

        public void Dispose()
        {
            this.Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!this.disposed)
            {
                this.ResetFaceTracking();

                this.disposed = true;
            }
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            base.OnRender(drawingContext);
            foreach (SkeletonFaceTracker faceInformation in this.trackedSkeletons.Values)
            {
                faceInformation.DrawFaceModel(drawingContext);
            }
        }

        private void OnAllFramesReady(object sender, AllFramesReadyEventArgs allFramesReadyEventArgs)
        {
            ColorImageFrame colorImageFrame = null;
            DepthImageFrame depthImageFrame = null;
            SkeletonFrame skeletonFrame = null;

            try
            {
                colorImageFrame = allFramesReadyEventArgs.OpenColorImageFrame();
                depthImageFrame = allFramesReadyEventArgs.OpenDepthImageFrame();
                skeletonFrame = allFramesReadyEventArgs.OpenSkeletonFrame();

                if (colorImageFrame == null || depthImageFrame == null || skeletonFrame == null)
                {
                    return;
                }

                // Check for image format changes.  The FaceTracker doesn't
                // deal with that so we need to reset.
                if (this.depthImageFormat != depthImageFrame.Format)
                {
                    this.ResetFaceTracking();
                    this.depthImage = null;
                    this.depthImageFormat = depthImageFrame.Format;
                }

                if (this.colorImageFormat != colorImageFrame.Format)
                {
                    this.ResetFaceTracking();
                    this.colorImage = null;
                    this.colorImageFormat = colorImageFrame.Format;
                }

                // Create any buffers to store copies of the data we work with
                if (this.depthImage == null)
                {
                    this.depthImage = new short[depthImageFrame.PixelDataLength];
                }

                if (this.colorImage == null)
                {
                    this.colorImage = new byte[colorImageFrame.PixelDataLength];
                }

                // Get the skeleton information
                if (this.skeletonData == null || this.skeletonData.Length != skeletonFrame.SkeletonArrayLength)
                {
                    this.skeletonData = new Skeleton[skeletonFrame.SkeletonArrayLength];
                }

                colorImageFrame.CopyPixelDataTo(this.colorImage);
                depthImageFrame.CopyPixelDataTo(this.depthImage);
                skeletonFrame.CopySkeletonDataTo(this.skeletonData);

                // Update the list of trackers and the trackers with the current frame information
                foreach (Skeleton skeleton in this.skeletonData)
                {
                    if (skeleton.TrackingState == SkeletonTrackingState.Tracked
                        || skeleton.TrackingState == SkeletonTrackingState.PositionOnly)
                    {
                        // We want keep a record of any skeleton, tracked or untracked.
                        if (!this.trackedSkeletons.ContainsKey(skeleton.TrackingId))
                        {
                            this.trackedSkeletons.Add(skeleton.TrackingId, new SkeletonFaceTracker());
                        }

                        // Give each tracker the upated frame.

                        if (this.trackedSkeletons.TryGetValue(skeleton.TrackingId, out skeletonFaceTracker))
                        {
                            skeletonFaceTracker.OnFrameReady(this.Kinect, colorImageFormat, colorImage, depthImageFormat, depthImage, skeleton);
                            skeletonFaceTracker.LastTrackedFrame = skeletonFrame.FrameNumber;

                        }
                    }
                }

                this.RemoveOldTrackers(skeletonFrame.FrameNumber);

                this.InvalidateVisual();
            }
            finally
            {
                if (colorImageFrame != null)
                {
                    colorImageFrame.Dispose();
                }

                if (depthImageFrame != null)
                {
                    depthImageFrame.Dispose();
                }

                if (skeletonFrame != null)
                {
                    skeletonFrame.Dispose();
                }
            }
        }

        private void OnSensorChanged(KinectSensor oldSensor, KinectSensor newSensor)
        {
            if (oldSensor != null)
            {
                oldSensor.AllFramesReady -= this.OnAllFramesReady;
                this.ResetFaceTracking();
            }

            if (newSensor != null)
            {
                newSensor.AllFramesReady += this.OnAllFramesReady;
            }
        }

        /// <summary>
        /// Clear out any trackers for skeletons we haven't heard from for a while
        /// </summary>
        private void RemoveOldTrackers(int currentFrameNumber)
        {
            var trackersToRemove = new List<int>();

            foreach (var tracker in this.trackedSkeletons)
            {
                uint missedFrames = (uint)currentFrameNumber - (uint)tracker.Value.LastTrackedFrame;
                if (missedFrames > MaxMissedFrames)
                {
                    // There have been too many frames since we last saw this skeleton
                    trackersToRemove.Add(tracker.Key);
                }
            }

            foreach (int trackingId in trackersToRemove)
            {
                this.RemoveTracker(trackingId);
            }
        }

        private void RemoveTracker(int trackingId)
        {
            this.trackedSkeletons[trackingId].Dispose();
            this.trackedSkeletons.Remove(trackingId);
        }

        private void ResetFaceTracking()
        {
            foreach (int trackingId in new List<int>(this.trackedSkeletons.Keys))
            {
                this.RemoveTracker(trackingId);
            }
        }

        private class SkeletonFaceTracker : IDisposable
        {
            private static FaceTriangle[] faceTriangles;

            private EnumIndexableCollection<FeaturePoint, PointF> facePoints;

            private FaceTracker faceTracker;

            private bool lastFaceTrackSucceeded;

            private SkeletonTrackingState skeletonTrackingState;

            MLApp.MLApp matlab;

            FaceTrackFrame frame;

            EnumIndexableCollection<AnimationUnit, float> AUCoeff;

            int commandtoSend = 0;

            string result;

            List<String> numbersOnly;

            double[] inputarray;

            Vector3DF topSkull;

            private EnumIndexableCollection<FeaturePoint, Vector3DF> absfacePoints;

            public int LastTrackedFrame { get; set; }

            public void Dispose()
            {
                if (this.faceTracker != null)
                {
                    this.faceTracker.Dispose();
                    this.faceTracker = null;
                }
            }

            public void DrawFaceModel(DrawingContext drawingContext)
            {
                if (!this.lastFaceTrackSucceeded || this.skeletonTrackingState != SkeletonTrackingState.Tracked)
                {
                    return;
                }

                var faceModelPts = new List<Point>();
                var faceModel = new List<FaceModelTriangle>();

                for (int i = 0; i < this.facePoints.Count; i++)
                {
                    faceModelPts.Add(new Point(this.facePoints[i].X + 0.5f, this.facePoints[i].Y + 0.5f));
                }

                foreach (var t in faceTriangles)
                {
                    var triangle = new FaceModelTriangle();
                    triangle.P1 = faceModelPts[t.First];
                    triangle.P2 = faceModelPts[t.Second];
                    triangle.P3 = faceModelPts[t.Third];
                    faceModel.Add(triangle);
                }

                var faceModelGroup = new GeometryGroup();
                for (int i = 0; i < faceModel.Count; i++)
                {
                    var faceTriangle = new GeometryGroup();
                    faceTriangle.Children.Add(new LineGeometry(faceModel[i].P1, faceModel[i].P2));
                    faceTriangle.Children.Add(new LineGeometry(faceModel[i].P2, faceModel[i].P3));
                    faceTriangle.Children.Add(new LineGeometry(faceModel[i].P3, faceModel[i].P1));
                    faceModelGroup.Children.Add(faceTriangle);
                }

                drawingContext.DrawGeometry(Brushes.LightYellow, new Pen(Brushes.LightYellow, 1.0), faceModelGroup);
            }

            /// <summary>
            /// Updates the face tracking information for this skeleton
            /// </summary>
            /// 

            public void OnFrameReady(KinectSensor kinectSensor, ColorImageFormat colorImageFormat, byte[] colorImage, DepthImageFormat depthImageFormat, short[] depthImage, Skeleton skeletonOfInterest)
            {
                //Don't Touch
                this.skeletonTrackingState = skeletonOfInterest.TrackingState;
                if (this.skeletonTrackingState != SkeletonTrackingState.Tracked)
                {
                    return;
                }
                if (faceTracker == null)
                    faceTracker = new FaceTracker(kinectSensor);
                frame = this.faceTracker.Track(
                         colorImageFormat, colorImage, depthImageFormat, depthImage, skeletonOfInterest);
                this.lastFaceTrackSucceeded = frame.TrackSuccessful;
                if (this.lastFaceTrackSucceeded)
                {
                    if (faceTriangles == null)
                    {
                        faceTriangles = frame.GetTriangles();
                    }
                    this.facePoints = frame.GetProjected3DShape();

                    //Okay to touch
                    absfacePoints = frame.Get3DShape();
                    topSkull = absfacePoints[FeaturePoint.TopSkull];

                    /*
                    numbersOnly = new List<string>();
                    inputarray = new double[] { 0, 0 }; //this is just a test matrix, the real one will contain all face points
                    matlab = new MLApp.MLApp();
                    matlab.PutWorkspaceData("input", "base", inputarray);

                    result = (matlab.Execute("simpleNN(transpose(input))")); 

                    char[] delimiters = new char[] { };
                    string[] parts = result.Split(delimiters,
                             StringSplitOptions.RemoveEmptyEntries);
                    for (int i = 0; i < parts.Length; i++)
                    {
                        if (parts[i].Equals("ans")) { }
                        else if (parts[i].Equals("=")) { }
                        else { numbersOnly.Add(parts[i]); }
                    }
                    foreach (string str in numbersOnly)
                    {
                        float x = float.Parse(str);
                        Console.WriteLine(x);
                    }
               */
                    
                } 
            }

            private struct FaceModelTriangle
            {
                public Point P1;
                public Point P2;
                public Point P3;
            }

            public void sendCommand(int commandtoSend)
            {
                switch (commandtoSend)
                {
                    case 0:
                        msg = new OscMessage(myapp, "/move/w", 0.0f);
                        msg.Send(glovepie);
                        msg = new OscMessage(myapp, "/move/a", 0.0f);
                        msg.Send(glovepie);
                        msg = new OscMessage(myapp, "/move/s", 0.0f);
                        msg.Send(glovepie);
                        msg = new OscMessage(myapp, "/move/d", 0.0f);
                        msg.Send(glovepie);
                        msg = new OscMessage(myapp, "/move/lc", 0.0f);
                        msg.Send(glovepie);
                        msg = new OscMessage(myapp, "/move/rc", 0.0f);
                        msg.Send(glovepie);
                        msg = new OscMessage(myapp, "/move/middle", 0.0f);
                        msg.Send(glovepie);
                        break;
                    case 1:
                        msg = new OscMessage(myapp, "/move/w", 10.0f);
                        msg.Send(glovepie);
                        msg = new OscMessage(myapp, "/move/a", 0.0f);
                        msg.Send(glovepie);
                        msg = new OscMessage(myapp, "/move/s", 0.0f);
                        msg.Send(glovepie);
                        msg = new OscMessage(myapp, "/move/d", 0.0f);
                        msg.Send(glovepie);
                        msg = new OscMessage(myapp, "/move/lc", 0.0f);
                        msg.Send(glovepie);
                        msg = new OscMessage(myapp, "/move/rc", 0.0f);
                        msg.Send(glovepie);
                        msg = new OscMessage(myapp, "/move/space", 0.0f);
                        msg.Send(glovepie);
                        msg = new OscMessage(myapp, "/move/middle", 0.0f);
                        msg.Send(glovepie);
                        break;
                    case 2:
                        msg = new OscMessage(myapp, "/move/w", 0.0f);
                        msg.Send(glovepie);
                        msg = new OscMessage(myapp, "/move/a", 10.0f);
                        msg.Send(glovepie);
                        msg = new OscMessage(myapp, "/move/s", 0.0f);
                        msg.Send(glovepie);
                        msg = new OscMessage(myapp, "/move/d", 0.0f);
                        msg.Send(glovepie);
                        msg = new OscMessage(myapp, "/move/lc", 0.0f);
                        msg.Send(glovepie);
                        msg = new OscMessage(myapp, "/move/rc", 0.0f);
                        msg.Send(glovepie);
                        msg = new OscMessage(myapp, "/move/space", 0.0f);
                        msg.Send(glovepie);
                        msg = new OscMessage(myapp, "/move/middle", 0.0f);
                        msg.Send(glovepie);
                        break;
                    case 3:
                        msg = new OscMessage(myapp, "/move/w", 0.0f);
                        msg.Send(glovepie);
                        msg = new OscMessage(myapp, "/move/a", 0.0f);
                        msg.Send(glovepie);
                        msg = new OscMessage(myapp, "/move/s", 10.0f);
                        msg.Send(glovepie);
                        msg = new OscMessage(myapp, "/move/d", 0.0f);
                        msg.Send(glovepie);
                        msg = new OscMessage(myapp, "/move/lc", 0.0f);
                        msg.Send(glovepie);
                        msg = new OscMessage(myapp, "/move/rc", 0.0f);
                        msg.Send(glovepie);
                        msg = new OscMessage(myapp, "/move/space", 0.0f);
                        msg.Send(glovepie);
                        msg = new OscMessage(myapp, "/move/middle", 0.0f);
                        msg.Send(glovepie);
                        break;
                    case 4:
                        msg = new OscMessage(myapp, "/move/w", 0.0f);
                        msg.Send(glovepie);
                        msg = new OscMessage(myapp, "/move/a", 0.0f);
                        msg.Send(glovepie);
                        msg = new OscMessage(myapp, "/move/s", 0.0f);
                        msg.Send(glovepie);
                        msg = new OscMessage(myapp, "/move/d", 10.0f);
                        msg.Send(glovepie);
                        msg = new OscMessage(myapp, "/move/lc", 0.0f);
                        msg.Send(glovepie);
                        msg = new OscMessage(myapp, "/move/rc", 0.0f);
                        msg.Send(glovepie);
                        msg = new OscMessage(myapp, "/move/space", 0.0f);
                        msg.Send(glovepie);
                        msg = new OscMessage(myapp, "/move/middle", 0.0f);
                        msg.Send(glovepie);
                        break;
                    case 5:
                        msg = new OscMessage(myapp, "/move/w", 0.0f);
                        msg.Send(glovepie);
                        msg = new OscMessage(myapp, "/move/a", 0.0f);
                        msg.Send(glovepie);
                        msg = new OscMessage(myapp, "/move/s", 0.0f);
                        msg.Send(glovepie);
                        msg = new OscMessage(myapp, "/move/d", 0.0f);
                        msg.Send(glovepie);
                        msg = new OscMessage(myapp, "/move/lc", 10.0f);
                        msg.Send(glovepie);
                        msg = new OscMessage(myapp, "/move/rc", 0.0f);
                        msg.Send(glovepie);
                        msg = new OscMessage(myapp, "/move/space", 0.0f);
                        msg.Send(glovepie);
                        msg = new OscMessage(myapp, "/move/middle", 0.0f);
                        msg.Send(glovepie);
                        break;
                    case 6:
                        msg = new OscMessage(myapp, "/move/w", 0.0f);
                        msg.Send(glovepie);
                        msg = new OscMessage(myapp, "/move/a", 0.0f);
                        msg.Send(glovepie);
                        msg = new OscMessage(myapp, "/move/s", 0.0f);
                        msg.Send(glovepie);
                        msg = new OscMessage(myapp, "/move/d", 0.0f);
                        msg.Send(glovepie);
                        msg = new OscMessage(myapp, "/move/lc", 0.0f);
                        msg.Send(glovepie);
                        msg = new OscMessage(myapp, "/move/rc", 10.0f);
                        msg.Send(glovepie);
                        msg = new OscMessage(myapp, "/move/space", 0.0f);
                        msg.Send(glovepie);
                        msg = new OscMessage(myapp, "/move/middle", 0.0f);
                        msg.Send(glovepie);
                        break;
                    case 7:
                        msg = new OscMessage(myapp, "/move/w", 0.0f);
                        msg.Send(glovepie);
                        msg = new OscMessage(myapp, "/move/a", 0.0f);
                        msg.Send(glovepie);
                        msg = new OscMessage(myapp, "/move/s", 0.0f);
                        msg.Send(glovepie);
                        msg = new OscMessage(myapp, "/move/d", 0.0f);
                        msg.Send(glovepie);
                        msg = new OscMessage(myapp, "/move/lc", 0.0f);
                        msg.Send(glovepie);
                        msg = new OscMessage(myapp, "/move/rc", 0.0f);
                        msg.Send(glovepie);
                        msg = new OscMessage(myapp, "/move/space", 10.0f);
                        msg.Send(glovepie);
                        msg = new OscMessage(myapp, "/move/middle", 0.0f);
                        msg.Send(glovepie);
                        break;
                    case 8:
                        msg = new OscMessage(myapp, "/move/w", 0.0f);
                        msg.Send(glovepie);
                        msg = new OscMessage(myapp, "/move/a", 0.0f);
                        msg.Send(glovepie);
                        msg = new OscMessage(myapp, "/move/s", 0.0f);
                        msg.Send(glovepie);
                        msg = new OscMessage(myapp, "/move/d", 0.0f);
                        msg.Send(glovepie);
                        msg = new OscMessage(myapp, "/move/lc", 0.0f);
                        msg.Send(glovepie);
                        msg = new OscMessage(myapp, "/move/rc", 0.0f);
                        msg.Send(glovepie);
                        msg = new OscMessage(myapp, "/move/space", 0.0f);
                        msg.Send(glovepie);
                        msg = new OscMessage(myapp, "/move/middle", 10.0f);
                        msg.Send(glovepie);
                        break;
                }

            }

            public void writeFaceDataToFile(int number)
            {
                using (System.IO.StreamWriter file = new System.IO.StreamWriter(@"C:\Users\Bala\Desktop\Sophomore\Science Fair\Development and Optimization\Kinect\FaceData.txt", true))
                {
                    if (topSkull != null)
                    {
                        for (int absFaceCounter = 0; absFaceCounter < absfacePoints.Count; absFaceCounter++)
                        {
                            float xCoords = absfacePoints[absFaceCounter].X - topSkull.X;
                            float yCoords = absfacePoints[absFaceCounter].X - topSkull.X;
                            float zCoords = absfacePoints[absFaceCounter].X - topSkull.X;
                            file.Write(xCoords + " " + yCoords + " " + zCoords+" ");       
                        }
                        file.WriteLine();
                        Console.WriteLine("Data Logged");
                    }
                }
                using (System.IO.StreamWriter TargetFile = new System.IO.StreamWriter(@"C:\Users\Bala\Desktop\Sophomore\Science Fair\Development and Optimization\Kinect\Targets.txt", true))
                {
                    switch (number)
                    {
                        case 0: //nothing
                            TargetFile.WriteLine("1 0 0 0 0 0 0 0 0");
                            break;
                        case 1: //w
                            TargetFile.WriteLine("0 1 0 0 0 0 0 0 0");
                            break;
                        case 2: //a
                            TargetFile.WriteLine("0 0 1 0 0 0 0 0 0");
                            break;
                        case 3: //s
                            TargetFile.WriteLine("0 0 0 1 0 0 0 0 0");
                            break;
                        case 4: //d
                            TargetFile.WriteLine("0 0 0 0 1 0 0 0 0");
                            break;
                        case 5: //lc
                            TargetFile.WriteLine("0 0 0 0 0 1 0 0 0");
                            break;
                        case 6: //rc
                            TargetFile.WriteLine("0 0 0 0 0 0 1 0 0");
                            break;
                        case 7: //space
                            TargetFile.WriteLine("0 0 0 0 0 0 0 1 0");
                            break;
                        case 8: //middle mouse
                            TargetFile.WriteLine("0 0 0 0 0 0 0 0 1");
                            break;
                    }
                }
            }
        }

        private void UserControl_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == Key.A)
            {
                Console.WriteLine("Hello World");
                /* if (skeletonFaceTracker != null)
                 {
                     Console.WriteLine(skeletonFaceTracker.getJawData());
                 }*/
            }
        }

        public void writeFile(int number2)
        {
            skeletonFaceTracker.writeFaceDataToFile(number2);
        }
    }
}