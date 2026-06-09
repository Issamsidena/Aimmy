using AILogic;
using Vector2.Class;
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
using Point = System.Drawing.Point;
using System.Collections.Concurrent;
using System.Threading.Tasks;

namespace Vector2.AILogic
{
    internal class AIManager : IDisposable
    {
        #region Variables

        private int _currentImageSize;
        private readonly object _sizeLock = new object();
        private volatile bool _sizeChangePending = false;

        private double _currentMouseX = 0;
        private double _currentMouseY = 0;
        private bool _isAcquiringLock = false;
        // Snap lock settings
        private DateTime _lastAimExecutionTime = DateTime.MinValue;
        private Prediction _lastAimedTarget = null;
        private Stopwatch _approachTimer = new Stopwatch();
        private Point _approachStartPoint;
        private Rectangle _currentDetectionBox = Rectangle.Empty;
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
        private StaticPrediction staticPrediction;

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

        private double CenterXTranslated = 0;
        private double CenterYTranslated = 0;

        // Benchmarking
        private int iterationCount = 0;
        private long totalTime = 0;

        private int detectedX { get; set; }
        private int detectedY { get; set; }

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
            staticPrediction = new StaticPrediction();

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
                string executionProvider = Dictionary.dropdownState.ContainsKey("Execution Provider") 
                    ? Dictionary.dropdownState["Execution Provider"]
                    : "Cuda"; // Default to Cuda if not specified
                    try
                    {
                        // Try the selected execution provider first
                        await LoadModelAsync(sessionOptions, modelPath, executionProvider);
                    }
                    catch (Exception ex)
                    {
                        Log(LogLevel.Error, $"Error starting the model with {executionProvider}: {ex.Message}\n\nFalling back to Cpu...", true);
                        
                            try
                            {
                                // Final fallback to CPU
                                await LoadModelAsync(sessionOptions, modelPath, "CPU");
                            }
                            catch (Exception ex3)
                            {
                                Log(LogLevel.Error, $"Error starting the model via CPU: {ex3.Message}, you won't be able to aim assist at all.", true);
                            }
                    }

                    FileManager.CurrentlyLoadingModel = false;
                }
            }
        private Task LoadModelAsync(SessionOptions sessionOptions, string modelPath, string executionProvider)
        {
            try
            {
                ConfigureSessionForProvider(sessionOptions, executionProvider);
                
                // Load the model
                _onnxModel = new InferenceSession(modelPath, sessionOptions);
                _outputNames = new List<string>(_onnxModel.OutputMetadata.Keys);

                if (!ValidateOnnxShape())
                {
                    _onnxModel?.Dispose();
                    return Task.CompletedTask;
                }

                // pre-allocate bitmap buffer
                _bitmapBuffer = new byte[3 * IMAGE_SIZE * IMAGE_SIZE];
                
                Log(LogLevel.Info, $"Model loaded successfully with {executionProvider} provider");
            }
            catch (OnnxRuntimeException ex)
            {
                HandleOnnxRuntimeException(ex, executionProvider);
                throw;
            }
            catch (Exception ex)
            {
                Log(LogLevel.Error, $"Error loading the model with {executionProvider}: {ex.Message}", true);
                _onnxModel?.Dispose();
                throw;
            }

            _isAiLoopRunning = true;
            _aiLoopThread = new Thread(AiLoop)
            {
                IsBackground = true,
                Priority = ThreadPriority.AboveNormal
            };
            _aiLoopThread.Start();
            return Task.CompletedTask;
        }

        private void ConfigureSessionForProvider(SessionOptions sessionOptions, string executionProvider)
        {
            sessionOptions.EnableCpuMemArena = true;
            sessionOptions.EnableMemoryPattern = false;
            sessionOptions.GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL;
            sessionOptions.ExecutionMode = ExecutionMode.ORT_PARALLEL;
            sessionOptions.InterOpNumThreads = 1;
            sessionOptions.IntraOpNumThreads = 1;

            switch (executionProvider.ToUpper())
            {
                case "TENSORRT":
                    TryTensorRT(sessionOptions);
                    break;
                case "CUDA":
                    TryCUDA(sessionOptions);
                    break;
                default:
                    sessionOptions.AppendExecutionProvider_CPU();
                    break;
            }
        }

        private void TryTensorRT(SessionOptions sessionOptions)
        {
            try
            {
                var tensorrtOptions = new OrtTensorRTProviderOptions();
                tensorrtOptions.UpdateOptions(new Dictionary<string, string>
                {
                    { "device_id", "0" },
                    { "trt_fp16_enable", "1" },
                    { "trt_engine_cache_enable", "1" },
                    { "trt_engine_cache_path", "bin/tensorrt_cache" },
                    { "trt_timing_cache_enable", "1" }
                });
                
                sessionOptions.AppendExecutionProvider_Tensorrt(tensorrtOptions);
                Log(LogLevel.Info, "Using TensorRT provider. Expect long load times", true, 5000);
            }
            catch
            {
                TryCUDA(sessionOptions); // Fallback to CUDA
            }
        }

        private void TryCUDA(SessionOptions sessionOptions)
        {
            try
            {
                var cudaProviderOptions = new OrtCUDAProviderOptions();
                cudaProviderOptions.UpdateOptions(new Dictionary<string, string>
                {
                    { "device_id", "0" },
                    { "arena_extend_strategy", "kNextPowerOfTwo" },
                    { "do_copy_in_default_stream", "1" },
                    { "cudnn_conv_algo_search", "HEURISTIC" },
                });
                
                sessionOptions.AppendExecutionProvider_CUDA(cudaProviderOptions);
                Log(LogLevel.Info, "Using CUDA provider", true, 5000);
            }
            catch
            {
                sessionOptions.AppendExecutionProvider_CPU(); // Final fallback
                Log(LogLevel.Info, "Using CPU provider", true, 5000);
            }
        }
        private void HandleOnnxRuntimeException(OnnxRuntimeException ex, string executionProvider)
        {
            string message = null;
            string title = null;

            bool hasTensorRTError = ex.Message.Contains("TensorRT execution provider is not enabled in this build") ||
                                    (ex.Message.Contains("LoadLibrary failed with error 126") && ex.Message.Contains("onnxruntime_providers_tensorrt.dll"));

            bool hasCUDAError = ex.Message.Contains("CUDA execution provider is not enabled in this build") ||
                                (ex.Message.Contains("LoadLibrary failed with error 126") && ex.Message.Contains("onnxruntime_providers_cuda.dll"));

            if (hasTensorRTError && executionProvider.ToUpper() == "TENSORRT")
            {
                if (RequirementsManager.IsTensorRTInstalled())
                {
                    message = "TensorRT has been found by Vector, but not by ONNX. Please check your configuration.\nHint: Check CUDNN and your CUDA, and install dependencies to PATH correctly.";
                    title = "TensorRT Configuration Error";
                }
                else
                {
                    message = "TensorRT execution provider has not been found on your build. Please check your configuration.\nHint: Download TensorRT 10.3.x and install the LIB folder to PATH.";
                    title = "TensorRT Not Found";
                }
            }
            else if (hasCUDAError && executionProvider.ToUpper() == "CUDA")
            {
                if (RequirementsManager.IsCUDAInstalled() && RequirementsManager.IsCUDNNInstalled())
                {
                    message = "CUDA & CUDNN have been found by Vector, but not by ONNX. Please check your configuration.\nHint: Check CUDNN and your CUDA installations, path, etc. PATH directories should point directly towards the DLLs.";
                    title = "CUDA Configuration Error";
                }
                else
                {
                    message = "CUDA execution provider has not been found on your build. Please check your configuration.\nHint: Download CUDA 12.x. Then install CUDNN 9.x to your PATH (or install the DLL included with Vector).";
                    title = "CUDA Not Found";
                }
            }

            if (message != null)
            {
                Log(LogLevel.Error, message);
                Application.Current?.Dispatcher.Invoke(() =>
                {
                    MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);
                });
            }
            
            throw ex;
        }

        private bool ValidateOnnxShape()
        {
            if (_onnxModel == null) return false;

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
                    // For fixed models, store the expected input size
                    fixedInputSize = kvp.Value.Dimensions[2]; // Height = Width
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
                // For dynamic models, NUM_DETECTIONS depends on the currently selected image size
                NUM_DETECTIONS = CalculateNumDetections(IMAGE_SIZE);
                LoadClasses();
                ImageSizeUpdated?.Invoke(IMAGE_SIZE);
                Log(LogLevel.Info, $"Loaded dynamic model - using selected image size {IMAGE_SIZE}x{IMAGE_SIZE} with {NUM_DETECTIONS} detections", true, 3000);
            }
            else
            {
                // --- FIX: Always set NUM_DETECTIONS based on the model's fixed input size ---
                ModelFixedSize = fixedInputSize;
                NUM_DETECTIONS = CalculateNumDetections(fixedInputSize);   // <-- moved outside the conditional

                // List of supported sizes (for UI)
                var supportedSizes = new[] { "640", "512", "416", "320", "256", "160" };
                var fixedSizeStr = fixedInputSize.ToString();

                if (fixedInputSize != IMAGE_SIZE && supportedSizes.Contains(fixedSizeStr))
                {
                    // Auto‑adjust the image size setting
                    Log(LogLevel.Warning,
                        $"Fixed-size model expects {fixedInputSize}x{fixedInputSize}. Automatically adjusting Image Size setting.",
                        true, 3000);

                    Dictionary.dropdownState["Image Size"] = fixedSizeStr;

                    // --- FIX: Update internal state immediately ---
                    _currentImageSize = fixedInputSize;
                    // Invalidate cached tensors that depend on the old size
                    _reusableTensor = null;
                    _reusableInputArray = null;

                    // Update the UI dropdown if it exists
                    Application.Current?.Dispatcher.BeginInvoke(() =>
                    {
                        try
                        {
                            var mainWindow = Application.Current.Windows.OfType<MainWindow>().FirstOrDefault();
                            mainWindow?.SettingsMenuControlInstance?.UpdateImageSizeDropdown(fixedSizeStr);
                        }
                        catch { }
                    });

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

                // Validate the output shape using the correct NUM_DETECTIONS
                var expectedShape = new int[] { 1, 4 + NUM_CLASSES, NUM_DETECTIONS };
                if (!outputMetadata.Values.All(metadata => metadata.Dimensions.SequenceEqual(expectedShape)))
                {
                    Log(LogLevel.Error,
                        $"Output shape does not match the expected shape of {string.Join("x", expectedShape)}.\n" +
                        "This model will not work with Aimmy, please use a YOLOv8 model converted to ONNXv8.",
                        true, 10000);
                    return false;
                }

                Log(LogLevel.Info, $"Loaded fixed-size model: {fixedInputSize}x{fixedInputSize}", true, 2000);
            }

            // Notify UI about dynamic model status
            DynamicModelStatusChanged?.Invoke(IsDynamicModel);

            return true;
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
        private static bool ShouldPredict() =>
            Dictionary.toggleState["Show Detected Player"] ||
            Dictionary.toggleState["Constant AI Tracking"] ||
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
                // Cache dictionary values once per loop
                bool aimAssist = Dictionary.toggleState["Aim Assist"];
                bool showDetected = Dictionary.toggleState["Show Detected Player"];
                bool constantTracking = Dictionary.toggleState["Constant AI Tracking"];
                bool autoTrigger = Dictionary.toggleState["Auto Trigger"];
                bool predictions = Dictionary.toggleState["Predictions"];
                string detectionAreaType = Dictionary.dropdownState["Detection Area Type"];
                bool fovEnabled = Dictionary.toggleState["FOV"];
                
                lock (_sizeLock)
                {
                    if (_sizeChangePending) continue;
                }

                stopwatch.Restart();
                _captureManager.HandlePendingDisplayChanges();

                using (Benchmark("AILoopIteration"))
                {
                    if (fovEnabled) UpdateFOV();

                    if (aimAssist || showDetected || autoTrigger)
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
                                if (showDetected) DisableOverlay(DetectedPlayerOverlay!);
                                continue;
                            }

                            if (autoTrigger) await AutoTrigger();

                            CalculateCoordinates(DetectedPlayerOverlay, closestPrediction, _scaleX, _scaleY);

                            if (aimAssist) HandleAim(closestPrediction);

                            totalTime += stopwatch.ElapsedMilliseconds;
                            iterationCount++;
                        }
                        else await Task.Delay(1);
                    }
                    else await Task.Delay(1);
                }
                stopwatch.Stop();
            }
        }

        #region AI Loop Functions

        [MethodImpl(MethodImplOptions.AggressiveInlining)]

        private async Task AutoTrigger()
        {
            // if auto trigger is disabled,
            // or if NEITHER aim keybind is held,
            // or if constant AI tracking is enabled (which disables auto trigger),
            // we check for spray release and return
            bool isEitherAimKeyHeld = InputBindingManager.IsHoldingBinding("Aim Keybind") || 
                                    InputBindingManager.IsHoldingBinding("Second Aim Keybind");
            
            if (!Dictionary.toggleState["Auto Trigger"] ||
                !isEitherAimKeyHeld ||
                Dictionary.toggleState["Constant AI Tracking"])
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

            // if auto trigger is disabled, we reset the spray state
            // if the aim keybinds are not held, we reset the spray state
            bool shouldSpray = Dictionary.toggleState["Auto Trigger"] &&
                (InputBindingManager.IsHoldingBinding("Aim Keybind") && InputBindingManager.IsHoldingBinding("Second Aim Keybind")); //||
            //Dictionary.toggleState["Constant AI Tracking"];

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

            // Pre-calculate common values
            float rectCenterX = rect.X + rect.Width / 2;
            float rectBottomY = rect.Y + rect.Height;
            
            if (Dictionary.toggleState["X Axis Percentage Adjustment"])
            {
                detectedX = (int)((rect.X + (rect.Width * (XOffsetPercentage / 100))) * scaleX);
            }
            else
            {
                detectedX = (int)(rectCenterX * scaleX + XOffset);
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
                            
                            _lastAimExecutionTime = DateTime.Now;
                            _lastAimedTarget = closestPrediction;
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
                        
                        _lastAimExecutionTime = DateTime.Now;
                        _lastAimedTarget = closestPrediction;
                    }
                }
                else
                {
                    // boring normal aim
                    if (Dictionary.toggleState["Predictions"])
                    {
                        HandlePredictions(kalmanPrediction, closestPrediction, detectedX, detectedY);
                    }
                    else
                    {
                        MouseManager.MoveCrosshair(detectedX, detectedY);
                    }
                    
                    _lastAimExecutionTime = DateTime.Now;
                    _lastAimedTarget = closestPrediction;
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
                    
                case "Static Prediction":
                    StaticPrediction.StaticDetection staticDetection = new()
                    {
                        X = detectedX,
                        Y = detectedY,
                        Timestamp = DateTime.UtcNow
                    };

                    staticPrediction.UpdateDetection(staticDetection);
                    int staticDistance = (int)Dictionary.sliderSettings["Static Prediction Offset"];
                    var (predictedX, predictedY, hasPrediction) = staticPrediction.GetPredictedPosition(staticDistance);
                    
                    if (hasPrediction)
                    {
                        // Blend prediction with actual position
                        blendedX = (int)(predictedX * predictionBlend + detectedX * (1 - predictionBlend));
                        blendedY = (int)(predictedY * predictionBlend + detectedY * (1 - predictionBlend));
                        MouseManager.MoveCrosshair(blendedX, blendedY);
                    }
                    else
                    {
                        // No valid prediction, use current position
                        MouseManager.MoveCrosshair(detectedX, detectedY);
                    }
                    break; 
            }
        }
        private async Task<Prediction?> GetClosestPrediction(bool useMousePosition = true)
        {
            //whats these variables for? - taylor 
            //int adjustedTargetX, adjustedTargetY;
            //Lol they were there for emotional support - GK

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

            _currentDetectionBox = new Rectangle(targetX - IMAGE_SIZE / 2, targetY - IMAGE_SIZE / 2, IMAGE_SIZE, IMAGE_SIZE); // Detection box dynamic size

            Bitmap? frame;

            using (Benchmark("ScreenGrab"))
            {
                frame = _captureManager.ScreenGrab(_currentDetectionBox);
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
                    KDPredictions = PrepareKDTreeData(outputTensor, _currentDetectionBox, fovMinX, fovMaxX, fovMinY, fovMaxY);
                }

                if (KDPredictions.Count == 0)
                {
                    SaveFrame(frame);
                    return null;
                }
                // TODO: Optimize this linear search further if needed
                // TODO: Consider updating KD-Tree and adding options to switch from linear to kd.
                // we can honestly replacing linear search by letting sticky aim handle the search
                Prediction? bestCandidate = null;
                double bestDistSq = double.MaxValue;
                double center = IMAGE_SIZE / 2.0;

                using (Benchmark("LinearSearch"))
                {
                    foreach (var p in KDPredictions)
                    {
                        var dx = p.CenterXTranslated * IMAGE_SIZE - center;
                        var dy = p.CenterYTranslated * IMAGE_SIZE - center;
                        double d2 = dx * dx + dy * dy;
                        
                        // Penalize small targets by increasing their effective distance
                        double size = p.Rectangle.Width * p.Rectangle.Height;
                        double sizePenalty = 1.0 + (1000.0 / (size + 1.0)); // Small targets get bigger penalty
                        
                        double adjustedDistSq = d2 * sizePenalty;
                        
                        if (adjustedDistSq < bestDistSq)
                        {
                            bestDistSq = adjustedDistSq;
                            bestCandidate = p;
                        }
                    }
                }

                Prediction? finalTarget = HandleStickyAim(bestCandidate, KDPredictions);
                if (finalTarget != null)
                {
                    UpdateDetectionBox(finalTarget); // Removed detectionBox parameter
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

            // STEP 1: Find what the user is aiming at (closest to crosshair)
            Prediction? aimTarget = null;
            float nearestToCrosshairDistSq = float.MaxValue;

            foreach (var candidate in KDPredictions)
            {
                float distSq = GetDistanceSq(candidate.ScreenCenterX, candidate.ScreenCenterY, screenCenterX, screenCenterY);
                if (distSq < nearestToCrosshairDistSq)
                {
                    nearestToCrosshairDistSq = distSq;
                    aimTarget = candidate;
                }
            }

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
            float baseRadius = 50f; // Fixed base radius
            float sizeBonus = MathF.Min(30f, targetSize / 50f); // Max 30px bonus for large targets
            float trackingRadius = baseRadius + sizeBonus;
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
            return ratio > 3f ? 3f : (ratio < 1f ? 1f : ratio);
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

        private void UpdateDetectionBox(Prediction target)
        {
            if (_currentDetectionBox == Rectangle.Empty) return;
            
            float screenX = target.Rectangle.X + _currentDetectionBox.Left;
            float screenY = target.Rectangle.Y + _currentDetectionBox.Top;
            
            LastDetectionBox = new RectangleF(screenX, screenY, target.Rectangle.Width, target.Rectangle.Height);

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
            int selectedClassId = selectedClass == "Best Confidence" ? -1 : _modelClasses.FirstOrDefault(c => c.Value == selectedClass).Key;
            
            int detections = NUM_DETECTIONS;
            
            // First pass: confidences (always sequential - it's cheap)
            float[] confidences = new float[detections];
            int[] classIds = new int[detections];
            
            // Quick confidence check - sequential is fine for this
            for (int i = 0; i < detections; i++)
            {
                if (NUM_CLASSES == 1)
                {
                    confidences[i] = outputTensor[0, 4, i];
                    classIds[i] = 0;
                }
                else if (selectedClassId != -1)
                {
                    confidences[i] = outputTensor[0, 4 + selectedClassId, i];
                    classIds[i] = selectedClassId;
                }
                else
                {
                    float bestConf = 0;
                    int bestId = 0;
                    for (int c = 0; c < NUM_CLASSES; c++)
                    {
                        float classConf = outputTensor[0, 4 + c, i];
                        if (classConf > bestConf)
                        {
                            bestConf = classConf;
                            bestId = c;
                        }
                    }
                    confidences[i] = bestConf;
                    classIds[i] = bestId;
                }
            }
            
            // Count high-confidence detections
            int highConfidenceCount = 0;
            for (int i = 0; i < detections; i++)
            {
                if (confidences[i] >= minConfidence)
                    highConfidenceCount++;
            }
            
            // Decide whether to use parallel processing based on workload
            bool useParallel = highConfidenceCount > 100; // Threshold - adjust as needed
            var predictions = new List<Prediction>(highConfidenceCount);
            
            if (useParallel)
            {
                // Use parallel for heavy workload
                var lockObj = new object();
                
                Parallel.For(0, detections, i =>
                {
                    if (confidences[i] < minConfidence) return;
                    
                    float x_center = outputTensor[0, 0, i];
                    float y_center = outputTensor[0, 1, i];
                    float width = outputTensor[0, 2, i];
                    float height = outputTensor[0, 3, i];
                    
                    float halfWidth = width / 2;
                    float halfHeight = height / 2;
                    
                    if (x_center - halfWidth < fovMinX || x_center + halfWidth > fovMaxX ||
                        y_center - halfHeight < fovMinY || y_center + halfHeight > fovMaxY)
                        return;
                    
                    var prediction = new Prediction
                    {
                        Rectangle = new RectangleF(x_center - halfWidth, y_center - halfHeight, width, height),
                        Confidence = confidences[i],
                        ClassId = classIds[i],
                        ClassName = _modelClasses.GetValueOrDefault(classIds[i], $"Class_{classIds[i]}"),
                        CenterXTranslated = x_center / IMAGE_SIZE,
                        CenterYTranslated = y_center / IMAGE_SIZE,
                        ScreenCenterX = detectionBox.Left + x_center,
                        ScreenCenterY = detectionBox.Top + y_center
                    };
                    
                    lock (lockObj)
                    {
                        predictions.Add(prediction);
                    }
                });
            }
            else
            {
                // Use sequential for light workload (avoids threading overhead)
                for (int i = 0; i < detections; i++)
                {
                    if (confidences[i] < minConfidence) continue;
                    
                    float x_center = outputTensor[0, 0, i];
                    float y_center = outputTensor[0, 1, i];
                    float width = outputTensor[0, 2, i];
                    float height = outputTensor[0, 3, i];
                    
                    float halfWidth = width / 2;
                    float halfHeight = height / 2;
                    
                    if (x_center - halfWidth < fovMinX || x_center + halfWidth > fovMaxX ||
                        y_center - halfHeight < fovMinY || y_center + halfHeight > fovMaxY)
                        continue;
                    
                    predictions.Add(new Prediction
                    {
                        Rectangle = new RectangleF(x_center - halfWidth, y_center - halfHeight, width, height),
                        Confidence = confidences[i],
                        ClassId = classIds[i],
                        ClassName = _modelClasses.GetValueOrDefault(classIds[i], $"Class_{classIds[i]}"),
                        CenterXTranslated = x_center / IMAGE_SIZE,
                        CenterYTranslated = y_center / IMAGE_SIZE,
                        ScreenCenterX = detectionBox.Left + x_center,
                        ScreenCenterY = detectionBox.Top + y_center
                    });
                }
            }
            
            return predictions;
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
            // signal that we're shutting down
            lock (_sizeLock)
            {
                _sizeChangePending = true;
            }

            // stop the loop
            _isAiLoopRunning = false;
            if (_aiLoopThread != null && _aiLoopThread.IsAlive)
            {
                if (!_aiLoopThread.Join(TimeSpan.FromSeconds(1)))
                {
                    try { _aiLoopThread.Interrupt(); }
                    catch { }
                }
            }

            PrintBenchmarks();

            _captureManager.Dispose();

            // clean up misc resources
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