using Vector2.Class;
using System.Diagnostics;

namespace AILogic
{
    internal class KalmanPrediction
    {
        public struct Detection
        {
            public int X;
            public int Y;
            public DateTime Timestamp;
        }

        // State variables
        private double _x, _y, _vx, _vy;
        private double _px, _py, _pvx, _pvy;
        private DateTime _lastUpdateTime;
        private bool _initialized = false;
        
        // Velocity history for smoothing
        private Queue<double> _vxHistory = new Queue<double>(5);
        private Queue<double> _vyHistory = new Queue<double>(5);
        
        // Last prediction for deadzone
        private int _lastPredictedX, _lastPredictedY;
        
        // Tuning parameters - MUCH heavier smoothing
        private const double POSITION_PROCESS_NOISE = 0.07;      // Reduced 10x
        private const double VELOCITY_PROCESS_NOISE = 0.7;       // Reduced 10x
        private const double MEASUREMENT_NOISE = 8.0;           // Increased 4x (trust measurements less)
        private const double MAX_VELOCITY = 1000.0;               // Reduced max velocity
        private const double MIN_CONFIDENCE = 0.1;                // Minimum Kalman gain
        private double VELOCITY_SMOOTHING = (double)Dictionary.sliderSettings["Kalman Smoothness"];
        private const int DEADZONE_THRESHOLD = 1;                  // Don't move if prediction changes by less than 2 pixels

        public KalmanPrediction()
        {
            Reset();
        }

        public void UpdateKalmanFilter(Detection detection)
        {
            DateTime now = detection.Timestamp;
            
            if (!_initialized)
            {
                _x = detection.X;
                _y = detection.Y;
                _vx = 0;
                _vy = 0;
                
                _px = 100;
                _py = 100;
                _pvx = 100;
                _pvy = 100;
                
                // Clear history
                _vxHistory.Clear();
                _vyHistory.Clear();
                for (int i = 0; i < 5; i++)
                {
                    _vxHistory.Enqueue(0);
                    _vyHistory.Enqueue(0);
                }
                
                _lastUpdateTime = now;
                _initialized = true;
                return;
            }

            double dt = (now - _lastUpdateTime).TotalSeconds;
            dt = Math.Clamp(dt, 0.001, 0.1);

            // PREDICTION STEP
            double predX = _x + _vx * dt;
            double predY = _y + _vy * dt;
            
            // Much smaller uncertainty growth
            _px += POSITION_PROCESS_NOISE * dt;
            _py += POSITION_PROCESS_NOISE * dt;
            _pvx += VELOCITY_PROCESS_NOISE * dt;
            _pvy += VELOCITY_PROCESS_NOISE * dt;
            
            // Cap uncertainty to prevent runaway
            _px = Math.Min(_px, 100);
            _py = Math.Min(_py, 100);
            _pvx = Math.Min(_pvx, 100);
            _pvy = Math.Min(_pvy, 100);

            // UPDATE STEP
            double innovX = detection.X - predX;
            double innovY = detection.Y - predY;
            
            // Calculate innovation magnitude
            double innovMagnitude = Math.Sqrt(innovX * innovX + innovY * innovY);
            
            // Adaptive gain based on innovation size
            // If innovation is large (target moved a lot), increase gain slightly
            // But still keep it low overall
            double baseGain = 0.15; // Very low base gain
            double adaptiveBoost = Math.Min(0.2, innovMagnitude / 100.0);
            double adaptiveGain = Math.Clamp(baseGain + adaptiveBoost, 0.1, 0.35);
            
            // Kalman gains (squash them to be very conservative)
            double kx = Math.Min(_px / (_px + MEASUREMENT_NOISE), adaptiveGain);
            double ky = Math.Min(_py / (_py + MEASUREMENT_NOISE), adaptiveGain);

            // Update position with very conservative gain
            _x = predX + kx * innovX;
            _y = predY + ky * innovY;
            
            // Calculate raw velocity
            double rawVx = innovX / dt;
            double rawVy = innovY / dt;
            
            // Clamp raw velocity to prevent spikes
            rawVx = Math.Clamp(rawVx, -MAX_VELOCITY, MAX_VELOCITY);
            rawVy = Math.Clamp(rawVy, -MAX_VELOCITY, MAX_VELOCITY);
            
            // Add to history
            _vxHistory.Enqueue(rawVx);
            _vyHistory.Enqueue(rawVy);
            if (_vxHistory.Count > 5)
            {
                _vxHistory.Dequeue();
                _vyHistory.Dequeue();
            }
            
            // Use median instead of mean for velocity (rejects outliers)
            double smoothedVx = Median(_vxHistory.ToArray());
            double smoothedVy = Median(_vyHistory.ToArray());
            
            // Heavy EMA smoothing on velocity
            _vx = _vx * VELOCITY_SMOOTHING + smoothedVx * (1 - VELOCITY_SMOOTHING);
            _vy = _vy * VELOCITY_SMOOTHING + smoothedVy * (1 - VELOCITY_SMOOTHING);
            
            // Final velocity clamp
            _vx = Math.Clamp(_vx, -MAX_VELOCITY * 0.5, MAX_VELOCITY * 0.5); // Even lower max for smoothness
            _vy = Math.Clamp(_vy, -MAX_VELOCITY * 0.5, MAX_VELOCITY * 0.5);
            
            // Update covariance
            _px *= (1 - kx);
            _py *= (1 - ky);
            _pvx *= 0.95; // Decay velocity uncertainty
            _pvy *= 0.95;
            
            _lastUpdateTime = now;
        }

        public Detection GetKalmanPosition(double mouseSpeed = 0)
        {
            DateTime now = DateTime.UtcNow;
            double dt = (now - _lastUpdateTime).TotalSeconds;
            dt = Math.Clamp(dt, 0.001, 0.1);

            // Current position
            double currentX = _x + _vx * dt;
            double currentY = _y + _vy * dt;
            
            // Only predict if velocity is significant
            double speed = Math.Sqrt(_vx * _vx + _vy * _vy);
            double leadTime = 0;
            
            if (speed > 10) // Only predict if moving faster than 10 pixels/sec
            {
                leadTime = (double)Dictionary.sliderSettings["Kalman Lead Time"];
                
                // Reduce lead time based on speed (less prediction for fast targets to prevent overshoot)
                if (speed > 100)
                {
                    leadTime *= (100 / speed);
                }
            }
            
            // Predict future position
            int predictedX = (int)(currentX + _vx * leadTime);
            int predictedY = (int)(currentY + _vy * leadTime);
            
            // // Apply deadzone - don't change prediction if movement is tiny
            // if (Math.Abs(predictedX - _lastPredictedX) < DEADZONE_THRESHOLD)
            //     predictedX = _lastPredictedX;
            // if (Math.Abs(predictedY - _lastPredictedY) < DEADZONE_THRESHOLD)
            //     predictedY = _lastPredictedY;
            
            _lastPredictedX = predictedX;
            _lastPredictedY = predictedY;

            return new Detection
            {
                X = predictedX,
                Y = predictedY,
                Timestamp = now
            };
        }

        private double Median(double[] values)
        {
            if (values.Length == 0) return 0;
            var sorted = values.OrderBy(x => x).ToArray();
            return sorted[sorted.Length / 2];
        }

        public void Reset()
        {
            _x = _y = _vx = _vy = 0;
            _px = _py = _pvx = _pvy = 1.0;
            _initialized = false;
            _lastPredictedX = _lastPredictedY = 0;
            _vxHistory.Clear();
            _vyHistory.Clear();
        }
    }

    internal class WiseTheFoxPrediction
    {
        /// <summary>
        /// Prediction using Exponential Moving Average with velocity tracking
        /// Originally by @wisethef0x, fixed to include actual prediction
        /// </summary>
        public struct WTFDetection
        {
            public int X;
            public int Y;
            public DateTime Timestamp;
        }

        private DateTime _lastUpdateTime;
        private const double Alpha = 0.5; // Smoothing factor

        private double _emaX, _emaY;
        private double _velocityX, _velocityY;
        private double _prevX, _prevY;
        private bool _initialized = false;

        public WiseTheFoxPrediction()
        {
            _lastUpdateTime = DateTime.UtcNow;
        }

        public void UpdateDetection(WTFDetection detection)
        {
            var now = DateTime.UtcNow;

            if (!_initialized)
            {
                _emaX = detection.X;
                _emaY = detection.Y;
                _prevX = detection.X;
                _prevY = detection.Y;
                _velocityX = 0;
                _velocityY = 0;
                _lastUpdateTime = now;
                _initialized = true;
                return;
            }

            // Calculate time delta, clamped
            double dt = (now - _lastUpdateTime).TotalSeconds;
            dt = Math.Clamp(dt, 0.001, 0.1);

            // Apply EMA to position
            _emaX = Alpha * detection.X + (1.0 - Alpha) * _emaX;
            _emaY = Alpha * detection.Y + (1.0 - Alpha) * _emaY;

            // Calculate velocity (pixels per second)
            double newVelocityX = (_emaX - _prevX) / dt;
            double newVelocityY = (_emaY - _prevY) / dt;

            // Apply EMA to velocity for smoothing
            _velocityX = Alpha * newVelocityX + (1.0 - Alpha) * _velocityX;
            _velocityY = Alpha * newVelocityY + (1.0 - Alpha) * _velocityY;

            // Store for next frame
            _prevX = _emaX;
            _prevY = _emaY;
            _lastUpdateTime = now;
        }

        public WTFDetection GetEstimatedPosition()
        {
            // Get lead time from settings
            double leadTime = (double)Dictionary.sliderSettings["WiseTheFox Lead Time"];

            // Predict where target will be after lead time
            double predictedX = _emaX + _velocityX * leadTime;
            double predictedY = _emaY + _velocityY * leadTime;

            return new WTFDetection
            {
                X = (int)predictedX,
                Y = (int)predictedY,
                Timestamp = DateTime.UtcNow
            };
        }

        public void Reset()
        {
            _emaX = _emaY = 0;
            _velocityX = _velocityY = 0;
            _prevX = _prevY = 0;
            _initialized = false;
        }
    }

    internal class ShalloePredictionV2
    {
        /// <summary>
        /// Velocity-based prediction using historical velocity averaging
        /// Fixed to use proper velocity calculation instead of broken position averaging
        /// </summary>
        private static List<int> _velocityXHistory = new(5);
        private static List<int> _velocityYHistory = new(5);

        private static int _prevX = 0;
        private static int _prevY = 0;
        private static bool _initialized = false;

        // Max velocity samples to keep
        private const int MaxHistorySize = 5;

        public static void UpdatePosition(int targetX, int targetY)
        {
            if (!_initialized)
            {
                _prevX = targetX;
                _prevY = targetY;
                _initialized = true;
                return;
            }

            // Calculate velocity (pixels per frame)
            int velocityX = targetX - _prevX;
            int velocityY = targetY - _prevY;

            // Add to history, removing oldest if full
            if (_velocityXHistory.Count >= MaxHistorySize)
            {
                _velocityXHistory.RemoveAt(0);
                _velocityYHistory.RemoveAt(0);
            }

            _velocityXHistory.Add(velocityX);
            _velocityYHistory.Add(velocityY);

            // Store current position for next frame
            _prevX = targetX;
            _prevY = targetY;
        }

        public static int GetSPX()
        {
            if (!_initialized || _velocityXHistory.Count == 0)
            {
                return _prevX;
            }

            // Get lead multiplier from settings
            double leadMultiplier = (double)Dictionary.sliderSettings["Shalloe Lead Multiplier"];

            // Calculate average velocity
            double avgVelocity = _velocityXHistory.Average();

            // Predict future position: current + (average velocity * lead multiplier)
            return (int)(_prevX + avgVelocity * leadMultiplier);
        }

        public static int GetSPY()
        {
            if (!_initialized || _velocityYHistory.Count == 0)
            {
                return _prevY;
            }

            // Get lead multiplier from settings
            double leadMultiplier = (double)Dictionary.sliderSettings["Shalloe Lead Multiplier"];

            // Calculate average velocity
            double avgVelocity = _velocityYHistory.Average();

            // Predict future position: current + (average velocity * lead multiplier)
            return (int)(_prevY + avgVelocity * leadMultiplier);
        }

        public static void Reset()
        {
            _velocityXHistory.Clear();
            _velocityYHistory.Clear();
            _prevX = _prevY = 0;
            _initialized = false;
        }
    }
    
    internal class StaticPrediction
    {
        /// <summary>
        /// Predicts a fixed distance ahead in the direction of movement
        /// </summary>
        public struct StaticDetection
        {
            public int X;
            public int Y;
            public DateTime Timestamp;
        }

        private StaticDetection lastDetection;
        private StaticDetection secondLastDetection;
        private bool hasEnoughData = false;
        private int lastPredictedX = 0;
        private int lastPredictedY = 0;
        private float predictionSmoothing = 0.87f; // Smoothing factor for prediction jumps

        public StaticPrediction()
        {
            lastDetection = new StaticDetection();
            secondLastDetection = new StaticDetection();
        }

        public void UpdateDetection(StaticDetection detection)
        {
            // Shift previous detection
            secondLastDetection = lastDetection;
            lastDetection = detection;
            
            // Need at least 2 detections to determine direction
            hasEnoughData = (lastDetection.Timestamp - secondLastDetection.Timestamp).TotalSeconds > 0;
        }

        public (int predictedX, int predictedY, bool hasPrediction) GetPredictedPosition(int pixelsAhead = 10)
        {
            if (!hasEnoughData)
            {
                // No prediction yet, return current position
                lastPredictedX = lastDetection.X;
                lastPredictedY = lastDetection.Y;
                return (lastDetection.X, lastDetection.Y, false);
            }

            // Calculate movement direction and distance
            double deltaTime = (lastDetection.Timestamp - secondLastDetection.Timestamp).TotalSeconds;
            if (deltaTime <= 0) 
            {
                return (lastDetection.X, lastDetection.Y, false);
            }

            // Calculate velocity (pixels per second)
            double velocityX = (lastDetection.X - secondLastDetection.X) / deltaTime;
            double velocityY = (lastDetection.Y - secondLastDetection.Y) / deltaTime;

            // Only predict if there's significant movement
            double speed = Math.Sqrt(velocityX * velocityX + velocityY * velocityY);
            if (speed < 10) // Minimum speed threshold (10 pixels per second)
            {
                // Target is mostly stationary, use current position
                lastPredictedX = lastDetection.X;
                lastPredictedY = lastDetection.Y;
                return (lastDetection.X, lastDetection.Y, false);
            }

            // Calculate movement direction (unit vector)
            double directionX = velocityX / speed;
            double directionY = velocityY / speed;

            // Predict static distance ahead
            int newPredictedX = lastDetection.X + (int)(directionX * pixelsAhead);
            int newPredictedY = lastDetection.Y + (int)(directionY * pixelsAhead);

            // Smooth the prediction to avoid jumps
            if (lastPredictedX != 0 || lastPredictedY != 0)
            {
                newPredictedX = (int)(newPredictedX * (1 - predictionSmoothing) + lastPredictedX * predictionSmoothing);
                newPredictedY = (int)(newPredictedY * (1 - predictionSmoothing) + lastPredictedY * predictionSmoothing);
            }

            lastPredictedX = newPredictedX;
            lastPredictedY = newPredictedY;

            return (newPredictedX, newPredictedY, true);
        }

        public void Reset()
        {
            hasEnoughData = false;
            lastPredictedX = 0;
            lastPredictedY = 0;
            lastDetection = new StaticDetection();
            secondLastDetection = new StaticDetection();
        }
    }
}