using AILogic;
using Aimmy2.Class;
using Class;
using InputLogic;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Newtonsoft.Json.Linq;
using Other;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using Visuality;
using static AILogic.MathUtil;
using static Other.LogManager;

namespace Aimmy2.AILogic
{
    internal class AIManager : IDisposable
    {
        #region Variables

        private int _currentImageSize;
        private readonly object _sizeLock = new object();
        private volatile bool _sizeChangePending = false;

        public void RequestSizeChange(int newSize)
        {
            lock (_sizeLock)
            {
                _sizeChangePending = true;
            }
        }

        // Dynamic properties instead of constants
        public int IMAGE_SIZE => _currentImageSize;
        private int NUM_DETECTIONS { get; set; } = 8400; // Will be set dynamically for dynamic models
        private bool IsDynamicModel { get; set; } = false;

        // Public static property to check if current loaded model is dynamic
        public static bool CurrentModelIsDynamic { get; private set; } = false;
        private int ModelFixedSize { get; set; } = 640; // Store the fixed size for non-dynamic models
        private int NUM_CLASSES { get; set; } = 1;
        private Dictionary<int, string> _modelClasses = new Dictionary<int, string>
        {
            { 0, "enemy" }
        };
        public Dictionary<int, string> ModelClasses => _modelClasses; // apparently this is better than making _modelClasses public
        public static event Action<Dictionary<int, string>>? ClassesUpdated;
        public static event Action<int>? ImageSizeUpdated;
        public static event Action<bool>? DynamicModelStatusChanged;

        private const int SAVE_FRAME_COOLDOWN_MS = 500;

        private DateTime lastSavedTime = DateTime.MinValue;
        private List<string>? _outputNames;
        private RectangleF LastDetectionBox;
        private KalmanPrediction kalmanPrediction;
        private WiseTheFoxPrediction wtfpredictionManager;

        private byte[]? _bitmapBuffer; // Reusable buffer for bitmap operations

        // Display-aware properties
        private int ScreenWidth => DisplayManager.ScreenWidth;
        private int ScreenHeight => DisplayManager.ScreenHeight;
        private int ScreenLeft => DisplayManager.ScreenLeft;
        private int ScreenTop => DisplayManager.ScreenTop;

        private readonly RunOptions? _modeloptions;
        private InferenceSession? _onnxModel;

        private Thread? _aiLoopThread;
        private volatile bool _isAiLoopRunning;

        // For Auto-Labelling Data System
        private bool PlayerFound = false;

        // Sticky-Aim
        private Prediction? _currentTarget = null;
        private int _consecutiveFramesWithoutTarget = 0;
        private const int MAX_FRAMES_WITHOUT_TARGET = 3; // Allow 3 frames of target loss

        // Enhanced Sticky Aim State
        private float _lastTargetVelocityX = 0f;
        private float _lastTargetVelocityY = 0f;
        private float _targetLockScore = 0f;           // Accumulated "stickiness" score
        private const float LOCK_SCORE_DECAY = 0.85f;  // Decay per frame when target not matched
        private const float LOCK_SCORE_GAIN = 15f;     // Gain per frame when target matched
        private const float MAX_LOCK_SCORE = 100f;     // Maximum accumulated score
        private const float REFERENCE_TARGET_SIZE = 10000f; // Reference area for "close" targets (approx 100x100)
        private int _framesWithoutMatch = 0;           // Consecutive frames where current target wasn't found

        // Persistent Target Lock state.
        // The lock is tracked by ABSOLUTE screen coordinates (frame-stable), NOT a Prediction
        // object, because the capture/detection box re-centers on the mouse every frame and a
        // box-local Rectangle from a previous frame would drift. We NEVER aim at a stored
        // prediction; we only ever match the lock to a detection in the CURRENT frame.
        private bool _hasLock = false;
        private float _lockedScreenX = 0f;
        private float _lockedScreenY = 0f;
        private float _lockedVelX = 0f;   // EMA of per-frame screen movement (constant-velocity predictor)
        private float _lockedVelY = 0f;
        private float _lockedArea = 0f;
        private DateTime _lastEngagedTime = DateTime.MinValue;
        private DateTime _lockLostSince = DateTime.MinValue; // when the locked enemy first went missing
        private const double ENGAGEMENT_GAP_MS = 250; // gap since last engaged frame => treat as a fresh engagement
        // How long the locked enemy must stay un-matched before we treat it as DEAD and auto-switch.
        // Long enough that a brief detection flicker on a still-alive enemy won't switch us off it,
        // short enough that switching after a real kill feels immediate.
        private const double LOCK_DEATH_CONFIRM_MS = 40;

        private double CenterXTranslated = 0;
        private double CenterYTranslated = 0;

        // Benchmarking
        private int iterationCount = 0;
        private long totalTime = 0;

        private int detectedX { get; set; }
        private int detectedY { get; set; }

        // Snap Lock state
        private double _currentMouseX = 0;
        private double _currentMouseY = 0;
        private bool _isAcquiringLock = false;
        private Stopwatch _approachTimer = new Stopwatch();
        private System.Drawing.Point _approachStartPoint;

        public double AIConf = 0;
        private static int targetX, targetY;

        // Pre-calculated values - now dynamic
        private float _scaleX => ScreenWidth / (float)IMAGE_SIZE;
        private float _scaleY => ScreenHeight / (float)IMAGE_SIZE;

        // Tensor reuse (model inference)
        private DenseTensor<float>? _reusableTensor;
        private float[]? _reusableInputArray;
        private List<NamedOnnxValue>? _reusableInputs;

        // Benchmarking
        private readonly Dictionary<string, BenchmarkData> _benchmarks = new();
        private readonly object _benchmarkLock = new();


        private readonly CaptureManager _captureManager = new();
        #endregion Variables

        #region Benchmarking

        private class BenchmarkData
        {
            public long TotalTime { get; set; }
            public int CallCount { get; set; }
            public long MinTime { get; set; } = long.MaxValue;
            public long MaxTime { get; set; }
            public double AverageTime => CallCount > 0 ? (double)TotalTime / CallCount : 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private IDisposable Benchmark(string name)
        {
            return new BenchmarkScope(this, name);
        }

        private class BenchmarkScope : IDisposable
        {
            private readonly AIManager _manager;
            private readonly string _name;
            private readonly Stopwatch _sw;

            public BenchmarkScope(AIManager manager, string name)
            {
                _manager = manager;
                _name = name;
                _sw = Stopwatch.StartNew();
            }

            public void Dispose()
            {
                _sw.Stop();
                _manager.RecordBenchmark(_name, _sw.ElapsedMilliseconds);
            }
        }

        private void RecordBenchmark(string name, long elapsedMs)
        {
            lock (_benchmarkLock)
            {
                if (!_benchmarks.TryGetValue(name, out var data))
                {
                    data = new BenchmarkData();
                    _benchmarks[name] = data;
                }

                data.TotalTime += elapsedMs;
                data.CallCount++;
                data.MinTime = Math.Min(data.MinTime, elapsedMs);
                data.MaxTime = Math.Max(data.MaxTime, elapsedMs);
            }
        }

        public void PrintBenchmarks()
        {
            lock (_benchmarkLock)
            {
                var lines = new List<string>
                {
                    "=== AIManager Performance Benchmarks ==="
                };

                foreach (var kvp in _benchmarks.OrderBy(x => x.Key))
                {
                    var data = kvp.Value;
                    lines.Add($"{kvp.Key}: Avg={data.AverageTime:F2}ms, Min={data.MinTime}ms, Max={data.MaxTime}ms, Count={data.CallCount}");
                }

                lines.Add($"Overall FPS: {(iterationCount > 0 ? 1000.0 / (totalTime / (double)iterationCount) : 0):F2}");

                //File.WriteAllLines("AIManager_Benchmarks.txt", lines);

                Log(LogLevel.Info, string.Join(Environment.NewLine, lines));
            }
        }

        #endregion Benchmarking

        public AIManager(string modelPath)
        {
            // Initialize the cached image size
            _currentImageSize = int.Parse(Dictionary.dropdownState["Image Size"]);

            // Initialize DXGI capture for current display
            if (Dictionary.dropdownState["Screen Capture Method"] == "DirectX")
            {
                _captureManager.InitializeDxgiDuplication();
            }

            kalmanPrediction = new KalmanPrediction();
            wtfpredictionManager = new WiseTheFoxPrediction();

            _modeloptions = new RunOptions();

            var sessionOptions = new SessionOptions
            {
                EnableCpuMemArena = true,
                EnableMemoryPattern = false,
                GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
                ExecutionMode = ExecutionMode.ORT_SEQUENTIAL,
                InterOpNumThreads = 1,
                IntraOpNumThreads = 4
            };

            // Attempt to load via DirectML (else fallback to CPU)
            Task.Run(() => InitializeModel(sessionOptions, modelPath));
        }

        #region Models

        private async Task InitializeModel(SessionOptions sessionOptions, string modelPath)
        {
            using (Benchmark("ModelInitialization"))
            {
                try
                {
                    await LoadModelAsync(sessionOptions, modelPath, useDirectML: true);
                }
                catch (Exception ex)
                {
                    Log(LogLevel.Error, $"Error starting the model via DirectML: {ex.Message}\n\nFalling back to CPU, performance may be poor.", true);

                    try
                    {
                        await LoadModelAsync(sessionOptions, modelPath, useDirectML: false);
                    }
                    catch (Exception e)
                    {
                        Log(LogLevel.Error, $"Error starting the model via CPU: {e.Message}, you won't be able to aim assist at all.", true);
                    }
                }

                FileManager.CurrentlyLoadingModel = false;
            }
        }

        private Task LoadModelAsync(SessionOptions sessionOptions, string modelPath, bool useDirectML)
        {
            try
            {
                if (useDirectML) { sessionOptions.AppendExecutionProvider_DML(); }
                else { sessionOptions.AppendExecutionProvider_CPU(); }

                _onnxModel = new InferenceSession(modelPath, sessionOptions);
                _outputNames = new List<string>(_onnxModel.OutputMetadata.Keys);

                // Validate the onnx model output shape (ensure model is OnnxV8)
                if (!ValidateOnnxShape())
                {
                    _onnxModel?.Dispose();
                    return Task.CompletedTask;
                }

                // Pre-allocate bitmap buffer
                _bitmapBuffer = new byte[3 * IMAGE_SIZE * IMAGE_SIZE];
            }
            catch (Exception ex)
            {
                Log(LogLevel.Error, $"Error loading the model: {ex.Message}", true);
                _onnxModel?.Dispose();
                return Task.CompletedTask;
            }

            // Begin the loop
            _isAiLoopRunning = true;
            _aiLoopThread = new Thread(AiLoop)
            {
                IsBackground = true,
                Priority = ThreadPriority.AboveNormal // Higher priority for AI thread
            };
            _aiLoopThread.Start();
            return Task.CompletedTask;
        }

        private bool ValidateOnnxShape()
        {
            if (_onnxModel != null)
            {
                var inputMetadata = _onnxModel.InputMetadata;
                var outputMetadata = _onnxModel.OutputMetadata;

                Log(LogLevel.Info, "=== Model Metadata ===");
                Log(LogLevel.Info, "Input Metadata:");

                bool isDynamic = false;
                int fixedInputSize = 0;

                foreach (var kvp in inputMetadata)
                {
                    string dimensionsStr = string.Join("x", kvp.Value.Dimensions);
                    Log(LogLevel.Info, $"  Name: {kvp.Key}, Dimensions: {dimensionsStr}");

                    // Check if model is dynamic (dimensions are -1)
                    if (kvp.Value.Dimensions.Any(d => d == -1))
                    {
                        isDynamic = true;
                    }
                    else if (kvp.Value.Dimensions.Length == 4)
                    {
                        // For fixed models, check if it's the expected format (1x3xHxW)
                        fixedInputSize = kvp.Value.Dimensions[2]; // Height should equal Width for square models
                    }
                }

                Log(LogLevel.Info, "Output Metadata:");
                foreach (var kvp in outputMetadata)
                {
                    string dimensionsStr = string.Join("x", kvp.Value.Dimensions);
                    Log(LogLevel.Info, $"  Name: {kvp.Key}, Dimensions: {dimensionsStr}");
                }

                IsDynamicModel = isDynamic;
                CurrentModelIsDynamic = isDynamic;

                if (IsDynamicModel)
                {
                    // For dynamic models, calculate NUM_DETECTIONS based on selected image size
                    NUM_DETECTIONS = CalculateNumDetections(IMAGE_SIZE);
                    LoadClasses();
                    ImageSizeUpdated?.Invoke(IMAGE_SIZE);
                    Log(LogLevel.Info, $"Loaded dynamic model - using selected image size {IMAGE_SIZE}x{IMAGE_SIZE} with {NUM_DETECTIONS} detections", true, 3000);
                }
                else
                {
                    // For fixed models, auto-adjust image size if needed
                    ModelFixedSize = fixedInputSize;

                    // List of supported sizes
                    var supportedSizes = new[] { "640", "512", "416", "320", "256", "160" };
                    var fixedSizeStr = fixedInputSize.ToString();

                    if (fixedInputSize != IMAGE_SIZE && supportedSizes.Contains(fixedSizeStr))
                    {
                        // Auto-adjust the image size to match the model
                        Log(LogLevel.Warning,
                            $"Fixed-size model expects {fixedInputSize}x{fixedInputSize}. Automatically adjusting Image Size setting.",
                            true, 3000);

                        Dictionary.dropdownState["Image Size"] = fixedSizeStr;

                        // Update the UI dropdown if it exists
                        Application.Current?.Dispatcher.BeginInvoke(() =>
                        {
                            try
                            {
                                // Find the MainWindow and update the dropdown
                                var mainWindow = Application.Current.Windows.OfType<MainWindow>().FirstOrDefault();
                                if (mainWindow?.SettingsMenuControlInstance != null)
                                {
                                    mainWindow.SettingsMenuControlInstance.UpdateImageSizeDropdown(fixedSizeStr);
                                }
                            }
                            catch { }
                        });

                        // The IMAGE_SIZE property will now return the correct value
                        NUM_DETECTIONS = CalculateNumDetections(fixedInputSize);
                        ImageSizeUpdated?.Invoke(fixedInputSize);
                    }
                    else if (!supportedSizes.Contains(fixedSizeStr))
                    {
                        Log(LogLevel.Error,
                            $"Model requires unsupported size {fixedInputSize}x{fixedInputSize}. Supported sizes are: {string.Join(", ", supportedSizes)}",
                            true, 10000);
                        return false;
                    }

                    LoadClasses();

                    // For static models, validate the expected shape
                    var expectedShape = new int[] { 1, 4 + NUM_CLASSES, NUM_DETECTIONS };
                    if (!outputMetadata.Values.All(metadata => metadata.Dimensions.SequenceEqual(expectedShape)))
                    {
                        Log(LogLevel.Error,
                            $"Output shape does not match the expected shape of {string.Join("x", expectedShape)}.\nThis model will not work with Aimmy, please use an YOLOv8 model converted to ONNXv8.",
                            true, 10000);
                        return false;
                    }

                    Log(LogLevel.Info, $"Loaded fixed-size model: {fixedInputSize}x{fixedInputSize}", true, 2000);
                }

                // Notify UI about dynamic model status
                DynamicModelStatusChanged?.Invoke(IsDynamicModel);

                return true;
            }

            return false;
        }

        private void LoadClasses()
        {
            if (_onnxModel == null) return;
            _modelClasses.Clear();

            try
            {
                var metadata = _onnxModel.ModelMetadata;

                if (metadata != null &&
                    metadata.CustomMetadataMap.TryGetValue("names", out string? value) &&
                    !string.IsNullOrEmpty(value))
                {
                    JObject data = JObject.Parse(value);
                    if (data != null && data.Type == JTokenType.Object)
                    {
                        //int maxClassId = -1;
                        foreach (var item in data)
                        {
                            if (int.TryParse(item.Key, out int classId) && item.Value.Type == JTokenType.String)
                            {
                                _modelClasses[classId] = item.Value.ToString();
                            }
                        }
                        NUM_CLASSES = _modelClasses.Count > 0 ? _modelClasses.Keys.Max() + 1 : 1;
                        Log(LogLevel.Info, $"Loaded {_modelClasses.Count} class(es) from model metadata: {data.ToString(Newtonsoft.Json.Formatting.None)}", false);
                    }
                    else
                    {
                        Log(LogLevel.Error, "Model metadata 'names' field is not a valid JSON object.", true);
                    }
                }
                else
                {
                    Log(LogLevel.Error, "Model metadata does not contain 'names' field for classes.", true);
                }
                ClassesUpdated?.Invoke(new Dictionary<int, string>(_modelClasses));
            }
            catch (Exception ex)
            {
                Log(LogLevel.Error, $"Error loading classes: {ex.Message}", true);
            }
        }

        #endregion Models

        #region AI

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsAutoTriggerActive() =>
            Dictionary.toggleState["Constant AI Shooting"] ||
            InputBindingManager.IsHoldingBinding("Auto Trigger Keybind");

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool ShouldPredict() =>
            Dictionary.toggleState["Show Detected Player"] ||
            Dictionary.toggleState["Constant AI Tracking"] ||
            (Dictionary.toggleState["Auto Trigger"] && IsAutoTriggerActive()) ||
            InputBindingManager.IsHoldingBinding("Aim Keybind") ||
            InputBindingManager.IsHoldingBinding("Second Aim Keybind");

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool ShouldProcess() =>
            Dictionary.toggleState["Aim Assist"] ||
            Dictionary.toggleState["Show Detected Player"] ||
            Dictionary.toggleState["Auto Trigger"];

        private async void AiLoop()
        {
            Stopwatch stopwatch = new();
            DetectedPlayerWindow? DetectedPlayerOverlay = Dictionary.DetectedPlayerOverlay;

            while (_isAiLoopRunning)
            {
                // Check for pending size changes at the start of each iteration
                lock (_sizeLock)
                {
                    if (_sizeChangePending)
                    {
                        // Skip this iteration to allow clean shutdown
                        continue;
                    }
                }

                stopwatch.Restart();

                // Handle any pending display changes
                _captureManager.HandlePendingDisplayChanges();

                using (Benchmark("AILoopIteration"))
                {
                    UpdateFOV();

                    if (ShouldProcess())
                    {
                        if (ShouldPredict())
                        {
                            Prediction? closestPrediction;
                            using (Benchmark("GetClosestPrediction"))
                            {
                                closestPrediction = await GetClosestPrediction();
                            }

                            if (closestPrediction == null)
                            {
                                DisableOverlay(DetectedPlayerOverlay!);
                                continue;
                            }

                            using (Benchmark("AutoTrigger"))
                            {
                                await AutoTrigger();
                            }

                            using (Benchmark("CalculateCoordinates"))
                            {
                                CalculateCoordinates(DetectedPlayerOverlay, closestPrediction, _scaleX, _scaleY);
                            }

                            using (Benchmark("HandleAim"))
                            {
                                HandleAim(closestPrediction);
                            }

                            totalTime += stopwatch.ElapsedMilliseconds;
                            iterationCount++;
                        }
                        else
                        {
                            // Processing so we are at the ready but not holding right/click.
                            await Task.Delay(1);
                        }
                    }
                    else
                    {
                        // No work to do—sleep briefly to free up CPU
                        await Task.Delay(1);
                    }
                }

                stopwatch.Stop();
            }
        }

        #region AI Loop Functions

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private async Task AutoTrigger()
        {
            if (!Dictionary.toggleState["Auto Trigger"] ||
                !IsAutoTriggerActive() ||
                Dictionary.toggleState["Constant AI Tracking"]) // this logic is a bit weird, but it works.
                                                                // but it might need to be revised
            {
                CheckSprayRelease();
                return;
            }


            if (Dictionary.toggleState["Spray Mode"])
            {
                await MouseManager.DoTriggerClick(LastDetectionBox);
                return;
            }


            if (Dictionary.toggleState["Cursor Check"])
            {
                var mousePos = WinAPICaller.GetCursorPosition();

                if (!DisplayManager.IsPointInCurrentDisplay(new System.Windows.Point(mousePos.X, mousePos.Y)))
                {
                    return;
                }

                if (LastDetectionBox.Contains(mousePos.X, mousePos.Y))
                {
                    await MouseManager.DoTriggerClick(LastDetectionBox);
                }
            }
            else
            {
                await MouseManager.DoTriggerClick();
            }

            if (!Dictionary.toggleState["Aim Assist"] || !Dictionary.toggleState["Show Detected Player"]) return;

        }
        private void CheckSprayRelease()
        {
            if (!Dictionary.toggleState["Spray Mode"]) return;

            bool shouldSpray = Dictionary.toggleState["Auto Trigger"] &&
                IsAutoTriggerActive();

            // spray mode might need to be revised - taylor
            if (!shouldSpray)
            {
                MouseManager.ResetSprayState();
            }
        }

        private async void UpdateFOV()
        {
            if (Dictionary.dropdownState["Detection Area Type"] == "Closest to Mouse" && Dictionary.toggleState["FOV"])
            {
                var mousePosition = WinAPICaller.GetCursorPosition();

                // Check if mouse is on the current display
                if (!DisplayManager.IsPointInCurrentDisplay(new System.Windows.Point(mousePosition.X, mousePosition.Y)))
                {
                    // Mouse is on a different display - don't update FOV position
                    return;
                }

                // Translate mouse position relative to current display
                var displayRelativeX = mousePosition.X - DisplayManager.ScreenLeft;
                var displayRelativeY = mousePosition.Y - DisplayManager.ScreenTop;

                await Application.Current.Dispatcher.BeginInvoke(() =>
                    Dictionary.FOVWindow.FOVStrictEnclosure.Margin = new Thickness(
                        Convert.ToInt16(displayRelativeX / WinAPICaller.scalingFactorX) - 320, // this is based off the window size, not the size of the model -whip
                        Convert.ToInt16(displayRelativeY / WinAPICaller.scalingFactorY) - 320, 0, 0));
            }
        }

        private static void DisableOverlay(DetectedPlayerWindow DetectedPlayerOverlay)
        {
            if (Dictionary.toggleState["Show Detected Player"] && Dictionary.DetectedPlayerOverlay != null)
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    if (Dictionary.toggleState["Show AI Confidence"])
                    {
                        DetectedPlayerOverlay!.DetectedPlayerConfidence.Opacity = 0;
                    }

                    if (Dictionary.toggleState["Show Tracers"])
                    {
                        DetectedPlayerOverlay!.DetectedTracers.Opacity = 0;
                    }

                    DetectedPlayerOverlay!.DetectedPlayerFocus.Opacity = 0;
                });
            }
        }

        private void UpdateOverlay(DetectedPlayerWindow DetectedPlayerOverlay, Prediction closestPrediction)
        {
            var scalingFactorX = WinAPICaller.scalingFactorX;
            var scalingFactorY = WinAPICaller.scalingFactorY;

            // Convert screen coordinates to display-relative coordinates
            var displayRelativeX = LastDetectionBox.X - DisplayManager.ScreenLeft;
            var displayRelativeY = LastDetectionBox.Y - DisplayManager.ScreenTop;

            // Calculate center position in display-relative coordinates
            var centerX = Convert.ToInt16(displayRelativeX / scalingFactorX) + (LastDetectionBox.Width / 2.0);
            var centerY = Convert.ToInt16(displayRelativeY / scalingFactorY);

            Application.Current.Dispatcher.Invoke(() =>
            {
                if (Dictionary.toggleState["Show AI Confidence"])
                {
                    DetectedPlayerOverlay.DetectedPlayerConfidence.Opacity = 1;
                    DetectedPlayerOverlay.DetectedPlayerConfidence.Content = $"{closestPrediction.ClassName}: {Math.Round((AIConf * 100), 2)}%";

                    var labelEstimatedHalfWidth = DetectedPlayerOverlay.DetectedPlayerConfidence.ActualWidth / 2.0;
                    DetectedPlayerOverlay.DetectedPlayerConfidence.Margin = new Thickness(
                        centerX - labelEstimatedHalfWidth,
                        centerY - DetectedPlayerOverlay.DetectedPlayerConfidence.ActualHeight - 2, 0, 0);
                }
                var showTracers = Dictionary.toggleState["Show Tracers"];
                DetectedPlayerOverlay.DetectedTracers.Opacity = showTracers ? 1 : 0;
                if (showTracers)
                {
                    var tracerPosition = Dictionary.dropdownState["Tracer Position"];

                    var boxTop = centerY;
                    var boxBottom = centerY + LastDetectionBox.Height;
                    var boxHorizontalCenter = centerX;
                    var boxVerticalCenter = centerY + (LastDetectionBox.Height / 2.0);
                    var boxLeft = centerX - (LastDetectionBox.Width / 2.0);
                    var boxRight = centerX + (LastDetectionBox.Width / 2.0);

                    switch (tracerPosition)
                    {
                        case "Top":
                            DetectedPlayerOverlay.DetectedTracers.X2 = boxHorizontalCenter;
                            DetectedPlayerOverlay.DetectedTracers.Y2 = boxTop;
                            break;

                        case "Bottom":
                            DetectedPlayerOverlay.DetectedTracers.X2 = boxHorizontalCenter;
                            DetectedPlayerOverlay.DetectedTracers.Y2 = boxBottom;
                            break;

                        case "Middle":
                            var screenHorizontalCenter = DisplayManager.ScreenWidth / (2.0 * WinAPICaller.scalingFactorX);
                            if (boxHorizontalCenter < screenHorizontalCenter)
                            {
                                // if the box is on the left half of the screen, aim for the right-middle of the box
                                DetectedPlayerOverlay.DetectedTracers.X2 = boxRight;
                                DetectedPlayerOverlay.DetectedTracers.Y2 = boxVerticalCenter;
                            }
                            else
                            {
                                // if the box is on the right half, aim for the left-middle
                                DetectedPlayerOverlay.DetectedTracers.X2 = boxLeft;
                                DetectedPlayerOverlay.DetectedTracers.Y2 = boxVerticalCenter;
                            }
                            break;

                        default:
                            // default to the bottom-center if the setting is unrecognized
                            DetectedPlayerOverlay.DetectedTracers.X2 = boxHorizontalCenter;
                            DetectedPlayerOverlay.DetectedTracers.Y2 = boxBottom;
                            break;
                    }
                }

                DetectedPlayerOverlay.Opacity = Dictionary.sliderSettings["Opacity"];

                DetectedPlayerOverlay.DetectedPlayerFocus.Opacity = 1;
                DetectedPlayerOverlay.DetectedPlayerFocus.Margin = new Thickness(
                    centerX - (LastDetectionBox.Width / 2.0), centerY, 0, 0);
                DetectedPlayerOverlay.DetectedPlayerFocus.Width = LastDetectionBox.Width;
                DetectedPlayerOverlay.DetectedPlayerFocus.Height = LastDetectionBox.Height;
            });
        }

        private void CalculateCoordinates(DetectedPlayerWindow DetectedPlayerOverlay, Prediction closestPrediction, float scaleX, float scaleY)
        {
            AIConf = closestPrediction.Confidence;

            if (Dictionary.toggleState["Show Detected Player"] && Dictionary.DetectedPlayerOverlay != null)
            {
                using (Benchmark("UpdateOverlay"))
                {
                    UpdateOverlay(DetectedPlayerOverlay!, closestPrediction);
                }
                if (!Dictionary.toggleState["Aim Assist"]) return;
            }

            double YOffset = Dictionary.sliderSettings["Y Offset (Up/Down)"];
            double XOffset = Dictionary.sliderSettings["X Offset (Left/Right)"];

            double YOffsetPercentage = Dictionary.sliderSettings["Y Offset (%)"];
            double XOffsetPercentage = Dictionary.sliderSettings["X Offset (%)"];

            var rect = closestPrediction.Rectangle;

            if (Dictionary.toggleState["X Axis Percentage Adjustment"])
            {
                detectedX = (int)((rect.X + (rect.Width * (XOffsetPercentage / 100))) * scaleX);
            }
            else
            {
                detectedX = (int)((rect.X + rect.Width / 2) * scaleX + XOffset);
            }

            if (Dictionary.toggleState["Y Axis Percentage Adjustment"])
            {
                detectedY = (int)((rect.Y + rect.Height - (rect.Height * (YOffsetPercentage / 100))) * scaleY + YOffset);
            }
            else
            {
                detectedY = CalculateDetectedY(scaleY, YOffset, closestPrediction);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int CalculateDetectedY(float scaleY, double YOffset, Prediction closestPrediction)
        {
            var rect = closestPrediction.Rectangle;
            float yBase = rect.Y;
            float yAdjustment = 0;

            switch (Dictionary.dropdownState["Aiming Boundaries Alignment"])
            {
                case "Center":
                    yAdjustment = rect.Height / 2;
                    break;

                case "Top":
                    // yBase is already at the top
                    break;

                case "Bottom":
                    yAdjustment = rect.Height;
                    break;
            }

            return (int)((yBase + yAdjustment) * scaleY + YOffset);
        }

        private void HandleAim(Prediction closestPrediction)
        {
            if (Dictionary.toggleState["Aim Assist"] &&
                (Dictionary.toggleState["Constant AI Tracking"] ||
                 Dictionary.toggleState["Aim Assist"] && InputBindingManager.IsHoldingBinding("Aim Keybind") ||
                 Dictionary.toggleState["Aim Assist"] && InputBindingManager.IsHoldingBinding("Second Aim Keybind")))
            {
                if (Dictionary.toggleState["Snap Lock"])
                {
                    var mousePos = WinAPICaller.GetCursorPosition();
                    _currentMouseX = mousePos.X;
                    _currentMouseY = mousePos.Y;

                    var targetCenterX = detectedX;
                    var targetCenterY = detectedY;
                    var distanceToTarget = Math.Sqrt(
                        Math.Pow(targetCenterX - _currentMouseX, 2) +
                        Math.Pow(targetCenterY - _currentMouseY, 2)
                    );
                    double approachThreshold = Dictionary.sliderSettings["Approach Threshold"];
                    double approachSpeed = Dictionary.sliderSettings["Approach Speed"];

                    if (distanceToTarget > approachThreshold)
                    {
                        if (!_isAcquiringLock)
                        {
                            _isAcquiringLock = true;
                            _approachTimer.Restart();
                            _approachStartPoint = mousePos;
                        }
                        // check if it should move (every 2-3 frames)
                        bool shouldMoveNow = _approachTimer.ElapsedMilliseconds % 33 < 16; // ~30 fps

                        if (shouldMoveNow)
                        {
                            // During approach, move directly to target but with reduced sensitivity
                            if (Dictionary.toggleState["Predictions"])
                            {
                                HandlePredictions(kalmanPrediction, closestPrediction, detectedX, detectedY);
                            }
                            else
                            {
                                double approachSensitivity = approachSpeed; // Different speed
                                int approachX = (int)(_currentMouseX + (detectedX - _currentMouseX) * approachSensitivity);
                                int approachY = (int)(_currentMouseY + (detectedY - _currentMouseY) * approachSensitivity);
                                MouseManager.MoveCrosshair(approachX, approachY);
                            }
                        }
                    }
                    else
                    {
                        // Go back to normal aiming
                        _isAcquiringLock = false;

                        if (Dictionary.toggleState["Predictions"])
                        {
                            HandlePredictions(kalmanPrediction, closestPrediction, detectedX, detectedY);
                        }
                        else
                        {
                            MouseManager.MoveCrosshair(detectedX, detectedY);
                        }
                    }
                }
                else
                {
                    if (Dictionary.toggleState["Predictions"])
                    {
                        HandlePredictions(kalmanPrediction, closestPrediction, detectedX, detectedY);
                    }
                    else
                    {
                        MouseManager.MoveCrosshair(detectedX, detectedY);
                    }
                }
            }
        }

        private void HandlePredictions(KalmanPrediction kalmanPrediction, Prediction closestPrediction, int detectedX, int detectedY)
        {
            var predictionMethod = Dictionary.dropdownState["Prediction Method"];
            float predictionBlend = (float)Dictionary.sliderSettings["Prediction Blend"] / 100f;
            switch (predictionMethod)
            {
                case "Kalman Filter":
                    KalmanPrediction.Detection detection = new()
                    {
                        X = detectedX,
                        Y = detectedY,
                        Timestamp = DateTime.UtcNow
                    };

                    kalmanPrediction.UpdateKalmanFilter(detection);
                    var predictedPosition = kalmanPrediction.GetKalmanPosition();

                    // Blend prediction with actual position
                    int blendedX = (int)(predictedPosition.X * predictionBlend + detectedX * (1 - predictionBlend));
                    int blendedY = (int)(predictedPosition.Y * predictionBlend + detectedY * (1 - predictionBlend));

                    MouseManager.MoveCrosshair(blendedX, blendedY);
                    break;

                case "Shall0e's Prediction":
                    // Update position (calculates velocity internally)
                    ShalloePredictionV2.UpdatePosition(detectedX, detectedY);

                    int spx = ShalloePredictionV2.GetSPX();
                    int spy = ShalloePredictionV2.GetSPY();

                    // Blend prediction with actual position
                    blendedX = (int)(spx * predictionBlend + detectedX * (1 - predictionBlend));
                    blendedY = (int)(spy * predictionBlend + detectedY * (1 - predictionBlend));

                    MouseManager.MoveCrosshair(blendedX, blendedY);
                    break;

                case "wisethef0x's EMA Prediction":
                    WiseTheFoxPrediction.WTFDetection wtfdetection = new()
                    {
                        X = detectedX,
                        Y = detectedY,
                        Timestamp = DateTime.UtcNow
                    };

                    wtfpredictionManager.UpdateDetection(wtfdetection);
                    var wtfpredictedPosition = wtfpredictionManager.GetEstimatedPosition();

                    // Blend prediction with actual position
                    blendedX = (int)(wtfpredictedPosition.X * predictionBlend + detectedX * (1 - predictionBlend));
                    blendedY = (int)(wtfpredictedPosition.Y * predictionBlend + detectedY * (1 - predictionBlend));

                    MouseManager.MoveCrosshair(blendedX, blendedY);
                    break;
            }
        }

        private async Task<Prediction?> GetClosestPrediction(bool useMousePosition = true)
        {
            //whats these variables for? - taylor 
            //int adjustedTargetX, adjustedTargetY;

            if (Dictionary.dropdownState["Detection Area Type"] == "Closest to Mouse")
            {
                var mousePos = WinAPICaller.GetCursorPosition();

                // Check if mouse is on the current display
                if (DisplayManager.IsPointInCurrentDisplay(new System.Windows.Point(mousePos.X, mousePos.Y)))
                {
                    // Mouse is on current display, use its position
                    targetX = mousePos.X;
                    targetY = mousePos.Y;
                }
                else
                {
                    // Mouse is on different display, use center of current display
                    targetX = DisplayManager.ScreenLeft + (DisplayManager.ScreenWidth / 2);
                    targetY = DisplayManager.ScreenTop + (DisplayManager.ScreenHeight / 2);
                }
            }
            else
            {
                // Center of current display
                targetX = DisplayManager.ScreenLeft + (DisplayManager.ScreenWidth / 2);
                targetY = DisplayManager.ScreenTop + (DisplayManager.ScreenHeight / 2);
            }

            Rectangle detectionBox = new(targetX - IMAGE_SIZE / 2, targetY - IMAGE_SIZE / 2, IMAGE_SIZE, IMAGE_SIZE); // Detection box dynamic size

            Bitmap? frame;

            using (Benchmark("ScreenGrab"))
            {
                frame = _captureManager.ScreenGrab(detectionBox);
            }

            if (frame == null) return null;

            IDisposableReadOnlyCollection<DisposableNamedOnnxValue>? results = null;
            Tensor<float>? outputTensor = null;

            try
            {
                float[] inputArray;
                using (Benchmark("BitmapToFloatArray"))
                {
                    if (_reusableInputArray == null || _reusableInputArray.Length != 3 * IMAGE_SIZE * IMAGE_SIZE)
                    {
                        _reusableInputArray = new float[3 * IMAGE_SIZE * IMAGE_SIZE];
                    }
                    inputArray = _reusableInputArray;

                    // Fill the reusable array
                    BitmapToFloatArrayInPlace(frame, inputArray, IMAGE_SIZE);
                }

                // Reuse tensor and inputs - recreate if size changed
                /// this needs to be revised !!!!! - taylor
                if (_reusableTensor == null || _reusableTensor.Dimensions[2] != IMAGE_SIZE)
                {
                    _reusableTensor = new DenseTensor<float>(inputArray, new int[] { 1, 3, IMAGE_SIZE, IMAGE_SIZE });
                    _reusableInputs = new List<NamedOnnxValue> { NamedOnnxValue.CreateFromTensor("images", _reusableTensor) };
                }
                else
                {
                    // Directly copy into existing DenseTensor buffer
                    inputArray.AsSpan().CopyTo(_reusableTensor.Buffer.Span);
                }

                if (_onnxModel == null) return null;
                using (Benchmark("ModelInference"))
                {
                    results = _onnxModel.Run(_reusableInputs, _outputNames, _modeloptions);
                    outputTensor = results[0].AsTensor<float>();
                }

                if (outputTensor == null)
                {
                    Log(LogLevel.Error, "Model inference returned null output tensor.", true, 2000);
                    SaveFrame(frame);
                    return null;
                }

                // Calculate the FOV boundaries
                float FovSize = (float)Dictionary.sliderSettings["FOV Size"];
                float fovMinX = (IMAGE_SIZE - FovSize) / 2.0f;
                float fovMaxX = (IMAGE_SIZE + FovSize) / 2.0f;
                float fovMinY = (IMAGE_SIZE - FovSize) / 2.0f;
                float fovMaxY = (IMAGE_SIZE + FovSize) / 2.0f;

                //List<double[]> KDpoints;
                List<Prediction> KDPredictions;
                using (Benchmark("PrepareKDTreeData"))
                {
                    KDPredictions = PrepareKDTreeData(outputTensor, detectionBox, fovMinX, fovMaxX, fovMinY, fovMaxY);
                }

                if (KDPredictions.Count == 0)
                {
                    SaveFrame(frame);
                    return null;
                }

                // Pick the target according to the user's Target Priority setting
                // (Best Confidence / Closest Distance / Closest Crosshair).
                Prediction? bestCandidate;
                using (Benchmark("LinearSearch"))
                {
                    string priorityMode = GetTargetPriorityMode();
                    bestCandidate = SelectBestPredictionByTargetPriority(
                        KDPredictions, priorityMode, IMAGE_SIZE / 2f, IMAGE_SIZE / 2f);
                }

                Prediction? finalTarget = Dictionary.toggleState["Persistent Target Lock"]
                    ? HandlePersistentLock(bestCandidate, KDPredictions)
                    : HandleStickyAim(bestCandidate, KDPredictions);
                if (finalTarget != null)
                {
                    UpdateDetectionBox(finalTarget, detectionBox);
                    SaveFrame(frame, finalTarget);
                    return finalTarget;
                }

                return null;
            }
            finally
            {
                // Always dispose the cloned frame to prevent memory leaks
                frame.Dispose();
                results?.Dispose();
            }
        }

        // Persistent Target Lock: commit to ONE target and stay on it until it DIES, then auto-switch.
        //
        // Behavior (per the user):
        //   * Pressing the aim key locks onto the enemy you are AIMING AT (nearest box to the
        //     crosshair), ignoring the Target Priority dropdown.
        //   * While that enemy is ALIVE we stay on it and never switch -- nearby/crossing enemies
        //     cannot steal the lock.
        //   * When the locked enemy DIES, we auto-switch to the enemy nearest the crosshair, with
        //     NO aim-key re-press. A short confirm window separates a real kill from a brief
        //     detection flicker so we don't switch off a still-alive enemy.
        //   * Releasing the aim key ends the engagement; pressing again acquires a fresh target.
        //
        // This NEVER returns a stale prediction: the only thing returned is a detection from the
        // CURRENT frame, matched to the lock by absolute screen position + size similarity.
        private Prediction? HandlePersistentLock(Prediction? bestCandidate, List<Prediction> KDPredictions)
        {
            bool constantTracking = Dictionary.toggleState["Constant AI Tracking"];
            bool aimKeyHeld = InputBindingManager.IsHoldingBinding("Aim Keybind") ||
                              InputBindingManager.IsHoldingBinding("Second Aim Keybind");
            bool engaged = constantTracking || aimKeyHeld;

            // Release on key up: not engaged => drop the lock so the next press acquires fresh.
            if (!engaged)
            {
                _hasLock = false;
                _lockLostSince = DateTime.MinValue;
                return bestCandidate;
            }

            // Fresh engagement: if this function didn't run for a while (aim key was released and
            // the AI loop went idle), treat this as a brand-new engagement.
            var now = DateTime.UtcNow;
            if ((now - _lastEngagedTime).TotalMilliseconds > ENGAGEMENT_GAP_MS)
            {
                _hasLock = false;
                _lockLostSince = DateTime.MinValue;
            }
            _lastEngagedTime = now;

            float crosshair = IMAGE_SIZE / 2f; // box-local crosshair position

            // No active lock yet: lock onto the enemy the user is AIMING AT (nearest box to the
            // crosshair). We intentionally IGNORE the Target Priority dropdown here -- persistent
            // lock commits to the enemy you point at, not the algorithm's "best confidence /
            // closest distance" pick.
            if (!_hasLock)
            {
                Prediction? acquire = SelectBestPredictionByTargetPriority(
                    KDPredictions, "Closest Crosshair", crosshair, crosshair);
                if (acquire == null) return null;
                SetLock(acquire);
                ResetPredictionFilters();
                _lockLostSince = DateTime.MinValue;
                return acquire;
            }

            // We have a lock. Find the SAME enemy in THIS frame and aim only at it. Every other
            // detection is ignored here, so nothing can steal the lock while the enemy is alive.
            Prediction? matched = MatchLockedTarget(KDPredictions);
            if (matched != null)
            {
                _lockLostSince = DateTime.MinValue;
                UpdateLock(matched); // track the locked enemy as it moves (position + velocity)
                return matched;
            }

            // Locked enemy not found this frame. Start/continue the death-confirm timer.
            if (_lockLostSince == DateTime.MinValue) _lockLostSince = now;

            if ((now - _lockLostSince).TotalMilliseconds < LOCK_DEATH_CONFIRM_MS)
            {
                // Probably a brief flicker, not a kill: hold the lock, but don't aim at a ghost.
                return null;
            }

            // The locked enemy has been gone long enough -> it's dead. Auto-switch to the enemy
            // nearest the crosshair (where we were just aiming), with no aim-key re-press.
            Prediction? next = SelectBestPredictionByTargetPriority(
                KDPredictions, "Closest Crosshair", crosshair, crosshair);
            if (next != null)
            {
                SetLock(next);
                ResetPredictionFilters(); // avoid overshoot/stutter when snapping to the new target
                _lockLostSince = DateTime.MinValue;
                return next;
            }

            // No enemy on screen yet; keep the lock cleared so we re-acquire the instant one appears.
            _hasLock = false;
            _lockLostSince = DateTime.MinValue;
            return null;
        }

        // Fresh acquire: snap the lock onto a brand-new target with no movement history.
        private void SetLock(Prediction target)
        {
            _hasLock = true;
            _lockedScreenX = target.ScreenCenterX;
            _lockedScreenY = target.ScreenCenterY;
            _lockedArea = target.Rectangle.Width * target.Rectangle.Height;
            _lockedVelX = 0f;
            _lockedVelY = 0f;
        }

        // Clear the aim-smoothing/prediction filters. Call this when the lock jumps to a brand-new
        // target so the predictor doesn't carry the old target's velocity and overshoot the new one
        // (the "jump, stop, jump" stutter on a death-switch).
        private void ResetPredictionFilters()
        {
            kalmanPrediction.Reset();
            wtfpredictionManager.Reset();
            ShalloePredictionV2.Reset();
        }

        // Continue an existing lock: update its position AND its velocity (EMA of per-frame
        // movement) so MatchLockedTarget can predict where it'll be next frame.
        private void UpdateLock(Prediction matched)
        {
            float dx = matched.ScreenCenterX - _lockedScreenX;
            float dy = matched.ScreenCenterY - _lockedScreenY;
            _lockedVelX = _lockedVelX * 0.5f + dx * 0.5f;
            _lockedVelY = _lockedVelY * 0.5f + dy * 0.5f;
            _lockedScreenX = matched.ScreenCenterX;
            _lockedScreenY = matched.ScreenCenterY;
            _lockedArea = matched.Rectangle.Width * matched.Rectangle.Height;
        }

        // Finds the detection in the current frame that is the SAME enemy as the lock. We match
        // against the PREDICTED position (last position + velocity), not the stale last position,
        // so a fast-moving live target stays matched (no false "lost target -> switch to best")
        // and an enemy crossing the other way is rejected because it's far from the prediction.
        private Prediction? MatchLockedTarget(List<Prediction> predictions)
        {
            float lockedSize = MathF.Sqrt(Math.Max(_lockedArea, 1f));
            // Because we match against the predicted position, this radius only needs to cover the
            // prediction error (acceleration/jitter), not the full per-frame movement. Keep it
            // moderate: tight enough that a different enemy can't be mistaken for the lock.
            float maxDist = Math.Clamp(lockedSize * 0.8f, 40f, 100f);
            float maxDistSq = maxDist * maxDist;

            float predX = _lockedScreenX + _lockedVelX;
            float predY = _lockedScreenY + _lockedVelY;

            Prediction? best = null;
            float bestDistSq = float.MaxValue;

            foreach (var p in predictions)
            {
                float area = p.Rectangle.Width * p.Rectangle.Height;
                float sizeRatio = MathF.Min(area, _lockedArea) / MathF.Max(area, Math.Max(_lockedArea, 1f));
                if (sizeRatio < 0.4f) continue; // too different in size to be the same enemy

                float distSq = GetDistanceSq(p.ScreenCenterX, p.ScreenCenterY, predX, predY);
                if (distSq < bestDistSq && distSq <= maxDistSq)
                {
                    bestDistSq = distSq;
                    best = p;
                }
            }

            return best;
        }

        private Prediction? HandleStickyAim(Prediction? bestCandidate, List<Prediction> KDPredictions)
        {
            if (!Dictionary.toggleState["Sticky Aim"])
            {
                _currentTarget = bestCandidate;
                ResetStickyAimState();
                return bestCandidate;
            }

            // No detections available
            if (bestCandidate == null || KDPredictions == null || KDPredictions.Count == 0)
            {
                return HandleNoDetections();
            }

            _consecutiveFramesWithoutTarget = 0;

            // Screen center (where user is aiming)
            float screenCenterX = IMAGE_SIZE / 2f;
            float screenCenterY = IMAGE_SIZE / 2f;

            // STEP 1: Find what the user is aiming at, respecting Target Priority.
            string stickyPriorityMode = GetTargetPriorityMode();
            Prediction? aimTarget = SelectBestPredictionByTargetPriority(
                KDPredictions, stickyPriorityMode, screenCenterX, screenCenterY);
            float nearestToCrosshairDistSq = aimTarget == null
                ? float.MaxValue
                : GetDistanceSq(aimTarget.ScreenCenterX, aimTarget.ScreenCenterY, screenCenterX, screenCenterY);

            if (aimTarget == null)
            {
                return HandleNoDetections();
            }

            // No current target - acquire what user is aiming at
            if (_currentTarget == null)
            {
                return AcquireNewTarget(aimTarget);
            }

            // STEP 2: Is the aim target the SAME as our current target?
            float lastX = _currentTarget.ScreenCenterX;
            float lastY = _currentTarget.ScreenCenterY;
            float targetArea = _currentTarget.Rectangle.Width * _currentTarget.Rectangle.Height;
            float targetSize = MathF.Sqrt(targetArea);
            float sizeFactor = GetSizeFactor(targetArea);

            // Distance from aim target to our current target's last position
            float aimToCurrentDistSq = GetDistanceSq(aimTarget.ScreenCenterX, aimTarget.ScreenCenterY, lastX, lastY);

            // Tracking radius based on target size - larger targets have larger radius
            float trackingRadius = targetSize * 3f;
            float trackingRadiusSq = trackingRadius * trackingRadius;

            // Check size similarity
            float aimTargetArea = aimTarget.Rectangle.Width * aimTarget.Rectangle.Height;
            float sizeRatio = MathF.Min(targetArea, aimTargetArea) / MathF.Max(targetArea, aimTargetArea);

            // Is the aim target the same as our current target?
            // Same if: close to last position AND similar size
            bool isSameTarget = (aimToCurrentDistSq < trackingRadiusSq) && (sizeRatio > 0.5f);

            if (isSameTarget)
            {
                // User is still aiming at current target - update and continue
                _framesWithoutMatch = 0;
                UpdateVelocity(aimTarget, sizeFactor);
                _targetLockScore = Math.Min(MAX_LOCK_SCORE, _targetLockScore + LOCK_SCORE_GAIN);
                _currentTarget = aimTarget;
                return aimTarget;
            }

            // STEP 3: User is aiming at a DIFFERENT target
            // But we need hysteresis - don't switch on single-frame jitter
            _framesWithoutMatch++;

            // Quick switch if aim target is very close to crosshair (user clearly aiming at it)
            float stickyThreshold = (float)Dictionary.sliderSettings["Sticky Aim Threshold"];
            bool aimTargetVeryCentered = nearestToCrosshairDistSq < (stickyThreshold * stickyThreshold * 0.25f);

            if (aimTargetVeryCentered || _framesWithoutMatch >= 3)
            {
                // User has clearly moved to new target - switch
                return AcquireNewTarget(aimTarget);
            }

            // Not ready to switch yet - return null to avoid flicking
            // (Don't return old target position, don't return new target position)
            return null;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float GetDistanceSq(float x1, float y1, float x2, float y2)
        {
            float dx = x1 - x2;
            float dy = y1 - y2;
            return dx * dx + dy * dy;
        }

        private static string GetTargetPriorityMode()
        {
            if (!Dictionary.dropdownState.TryGetValue("Target Priority", out var value))
                return "Best Confidence";

            return NormalizeTargetPriorityMode(value?.ToString());
        }

        // Maps legacy saved value "Closest Detection" to "Closest Distance".
        private static string NormalizeTargetPriorityMode(string? mode)
        {
            var m = string.IsNullOrWhiteSpace(mode) ? "Best Confidence" : mode.Trim();
            return m == "Closest Detection" ? "Closest Distance" : m;
        }

        // Distance from crosshair to the nearest point on an enemy box (0 if crosshair is on/over that enemy).
        private static float GetCrosshairToBoxDistanceSq(Prediction p, float crosshairX, float crosshairY)
        {
            var r = p.Rectangle;
            float nearestX = Math.Clamp(crosshairX, r.X, r.X + r.Width);
            float nearestY = Math.Clamp(crosshairY, r.Y, r.Y + r.Height);
            return GetDistanceSq(crosshairX, crosshairY, nearestX, nearestY);
        }

        // Best Confidence = highest AI score.
        // Closest Distance = largest detection box (near/large vs far/small on screen).
        // Closest Crosshair = enemy whose box is nearest to the crosshair point.
        private static Prediction? SelectBestPredictionByTargetPriority(
            IEnumerable<Prediction> predictions,
            string priorityMode,
            float crosshairX,
            float crosshairY)
        {
            string mode = NormalizeTargetPriorityMode(priorityMode);
            Prediction? best = null;
            double bestCrosshairDistSq = double.MaxValue;
            float bestBoxArea = float.MinValue;
            float bestConfidence = float.MinValue;

            foreach (var p in predictions)
            {
                switch (mode)
                {
                    case "Closest Crosshair":
                        {
                            float d2 = GetCrosshairToBoxDistanceSq(p, crosshairX, crosshairY);
                            if (d2 < bestCrosshairDistSq)
                            {
                                bestCrosshairDistSq = d2;
                                best = p;
                            }
                            break;
                        }
                    case "Closest Distance":
                        {
                            float area = p.Rectangle.Width * p.Rectangle.Height;
                            if (area > bestBoxArea)
                            {
                                bestBoxArea = area;
                                best = p;
                            }
                            break;
                        }
                    default:
                        {
                            if (p.Confidence > bestConfidence)
                            {
                                bestConfidence = p.Confidence;
                                best = p;
                            }
                            break;
                        }
                }
            }

            return best;
        }

        /// <summary>
        /// Returns a scaling factor based on target size. Smaller targets (further away) get higher factors
        /// to make thresholds more forgiving and filtering more aggressive.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private float GetSizeFactor(float targetArea)
        {
            // sizeFactor: 1.0 for large/close targets, up to 3.0 for small/distant targets
            // This makes distant targets more "sticky" to compensate for detection jitter
            float ratio = REFERENCE_TARGET_SIZE / Math.Max(targetArea, 100f);
            return Math.Clamp(ratio, 1.0f, 3.0f);
        }

        private Prediction? HandleNoDetections()
        {
            if (_currentTarget != null && ++_consecutiveFramesWithoutTarget <= MAX_FRAMES_WITHOUT_TARGET)
            {
                // Decay lock score during grace period
                _targetLockScore *= LOCK_SCORE_DECAY;

                // Return predicted position instead of stale position
                var predicted = new Prediction
                {
                    ScreenCenterX = _currentTarget.ScreenCenterX + _lastTargetVelocityX * _consecutiveFramesWithoutTarget,
                    ScreenCenterY = _currentTarget.ScreenCenterY + _lastTargetVelocityY * _consecutiveFramesWithoutTarget,
                    Rectangle = _currentTarget.Rectangle,
                    Confidence = _currentTarget.Confidence * (1f - _consecutiveFramesWithoutTarget * 0.2f),
                    ClassId = _currentTarget.ClassId,
                    ClassName = _currentTarget.ClassName,
                    CenterXTranslated = _currentTarget.CenterXTranslated,
                    CenterYTranslated = _currentTarget.CenterYTranslated
                };
                return predicted;
            }

            ResetStickyAimState();
            return null;
        }

        private Prediction AcquireNewTarget(Prediction target)
        {
            _lastTargetVelocityX = 0f;
            _lastTargetVelocityY = 0f;
            _targetLockScore = LOCK_SCORE_GAIN; // Start with some lock score
            _framesWithoutMatch = 0;
            _currentTarget = target;
            return target;
        }

        private void UpdateVelocity(Prediction newTarget, float sizeFactor)
        {
            if (_currentTarget != null)
            {
                // EMA smoothing on velocity to reduce noise
                // Use heavier smoothing for smaller/distant targets (more weight on old velocity)
                // sizeFactor 1.0 -> 0.7/0.3, sizeFactor 3.0 -> 0.9/0.1
                float smoothing = Math.Clamp(0.6f + (sizeFactor * 0.1f), 0.7f, 0.9f);
                float newWeight = 1f - smoothing;

                float newVelX = newTarget.ScreenCenterX - _currentTarget.ScreenCenterX;
                float newVelY = newTarget.ScreenCenterY - _currentTarget.ScreenCenterY;
                _lastTargetVelocityX = _lastTargetVelocityX * smoothing + newVelX * newWeight;
                _lastTargetVelocityY = _lastTargetVelocityY * smoothing + newVelY * newWeight;
            }
        }

        private void ResetStickyAimState()
        {
            _currentTarget = null;
            _consecutiveFramesWithoutTarget = 0;
            _framesWithoutMatch = 0;
            _lastTargetVelocityX = 0f;
            _lastTargetVelocityY = 0f;
            _targetLockScore = 0f;
        }

        private void UpdateDetectionBox(Prediction target, Rectangle detectionBox)
        {
            float translatedXMin = target.Rectangle.X + detectionBox.Left;
            float translatedYMin = target.Rectangle.Y + detectionBox.Top;
            LastDetectionBox = new(translatedXMin, translatedYMin,
                target.Rectangle.Width, target.Rectangle.Height);

            CenterXTranslated = target.CenterXTranslated;
            CenterYTranslated = target.CenterYTranslated;
        }
        // is it really kdtreedata though....
        private List<Prediction> PrepareKDTreeData(
            Tensor<float> outputTensor,
            Rectangle detectionBox,
            float fovMinX, float fovMaxX, float fovMinY, float fovMaxY)
        {
            float minConfidence = (float)Dictionary.sliderSettings["AI Minimum Confidence"] / 100.0f;
            string selectedClass = Dictionary.dropdownState["Target Class"];
            int selectedClassId = selectedClass == "Smart Detection" ? -1 : _modelClasses.FirstOrDefault(c => c.Value == selectedClass).Key;

            // we dont use kdpoints anymore because we replaced the kd-tree with a linear search
            //var KDpoints = new List<double[]>(NUM_DETECTIONS); // Pre-allocate with estimated capacity
            var KDpredictions = new List<Prediction>(NUM_DETECTIONS);

            for (int i = 0; i < NUM_DETECTIONS; i++)
            {
                float x_center = outputTensor[0, 0, i];
                float y_center = outputTensor[0, 1, i];
                float width = outputTensor[0, 2, i];
                float height = outputTensor[0, 3, i];

                int bestClassId = 0;
                float bestConfidence = 0f;

                if (NUM_CLASSES == 1)
                {
                    bestConfidence = outputTensor[0, 4, i];
                }
                else
                {
                    if (selectedClassId == -1)
                    {
                        for (int classId = 0; classId < NUM_CLASSES; classId++)
                        {
                            float classConfidence = outputTensor[0, 4 + classId, i];
                            if (classConfidence > bestConfidence)
                            {
                                bestConfidence = classConfidence;
                                bestClassId = classId;
                            }
                        }
                    }
                    else
                    {
                        bestConfidence = outputTensor[0, 4 + selectedClassId, i];
                        bestClassId = selectedClassId;
                    }
                }

                if (bestConfidence < minConfidence) continue;

                float x_min = x_center - width / 2;
                float y_min = y_center - height / 2;
                float x_max = x_center + width / 2;
                float y_max = y_center + height / 2;

                if (x_min < fovMinX || x_max > fovMaxX || y_min < fovMinY || y_max > fovMaxY) continue;

                RectangleF rect = new(x_min, y_min, width, height);
                Prediction prediction = new()
                {
                    Rectangle = rect,
                    Confidence = bestConfidence,
                    ClassId = bestClassId,
                    ClassName = _modelClasses.GetValueOrDefault(bestClassId, $"Class_{bestClassId}"),
                    CenterXTranslated = x_center / IMAGE_SIZE,
                    CenterYTranslated = y_center / IMAGE_SIZE,
                    ScreenCenterX = detectionBox.Left + x_center,
                    ScreenCenterY = detectionBox.Top + y_center
                };

                //KDpoints.Add(new double[] { x_center, y_center });
                KDpredictions.Add(prediction);
            }

            return KDpredictions;
        }

        #endregion AI Loop Functions

        #endregion AI

        #region Screen Capture

        private void SaveFrame(Bitmap frame, Prediction? DoLabel = null)
        {
            // Only save frames if "Collect Data While Playing" is enabled
            if (!Dictionary.toggleState["Collect Data While Playing"]) return;

            // Skip if we're in constant tracking mode (unless auto-labeling is enabled)
            if (Dictionary.toggleState["Constant AI Tracking"] && !Dictionary.toggleState["Auto Label Data"]) return;

            // Cooldown check
            if ((DateTime.Now - lastSavedTime).TotalMilliseconds < SAVE_FRAME_COOLDOWN_MS) return;

            try
            {
                // Validate bitmap is still usable
                if (frame == null) return;

                // Accessing Width/Height will throw if bitmap is disposed
                int width = frame.Width;
                int height = frame.Height;
                if (width <= 0 || height <= 0) return;

                lastSavedTime = DateTime.Now;
                string uuid = Guid.NewGuid().ToString();
                string imagePath = Path.Combine("bin", "images", $"{uuid}.jpg");

                // Save synchronously to avoid "Object is currently in use elsewhere" error
                frame.Save(imagePath, ImageFormat.Jpeg);

                if (Dictionary.toggleState["Auto Label Data"] && DoLabel != null)
                {
                    var labelPath = Path.Combine("bin", "labels", $"{uuid}.txt");

                    float x = (DoLabel!.Rectangle.X + DoLabel.Rectangle.Width / 2) / width;
                    float y = (DoLabel!.Rectangle.Y + DoLabel.Rectangle.Height / 2) / height;
                    float labelWidth = DoLabel.Rectangle.Width / width;
                    float labelHeight = DoLabel.Rectangle.Height / height;

                    File.WriteAllText(labelPath, $"{DoLabel.ClassId} {x} {y} {labelWidth} {labelHeight}");
                }
            }
            catch (ArgumentException)
            {
                // Bitmap was disposed or invalid - silently ignore
            }
            catch (Exception ex)
            {
                Log(LogLevel.Error, $"SaveFrame failed: {ex.Message}");
            }
        }



        #endregion Screen Capture

        public void Dispose()
        {
            // Signal that we're shutting down
            lock (_sizeLock)
            {
                _sizeChangePending = true;
            }

            // Stop the loop
            _isAiLoopRunning = false;
            if (_aiLoopThread != null && _aiLoopThread.IsAlive)
            {
                if (!_aiLoopThread.Join(TimeSpan.FromSeconds(1)))
                {
                    try { _aiLoopThread.Interrupt(); }
                    catch { }
                }
            }

            // Print final benchmarks
            PrintBenchmarks();

            // Dispose DXGI objects
            _captureManager.Dispose();

            // Clean up other resources
            _reusableInputArray = null;
            _reusableInputs = null;
            _onnxModel?.Dispose();
            _modeloptions?.Dispose();
            _bitmapBuffer = null;
        }
    }
    public class Prediction
    {
        public RectangleF Rectangle { get; set; }
        public float Confidence { get; set; }
        public int ClassId { get; set; } = 0;
        public string ClassName { get; set; } = "Enemy";
        public float CenterXTranslated { get; set; }
        public float CenterYTranslated { get; set; }
        public float ScreenCenterX { get; set; }  // Absolute screen position
        public float ScreenCenterY { get; set; }
    }
}