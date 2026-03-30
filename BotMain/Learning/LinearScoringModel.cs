using System;
using System.Globalization;
using System.Text;

namespace BotMain.Learning
{
    public sealed class LinearScoringModel
    {
        private readonly double[] _weights;
        private double _bias;
        private readonly double _learningRate;
        private readonly double _margin;

        public int FeatureCount => _weights.Length;

        public LinearScoringModel(int featureCount, double learningRate = 0.001, double margin = 0.5)
        {
            _weights = new double[featureCount];
            _bias = 0.0;
            _learningRate = learningRate;
            _margin = margin;
        }

        private LinearScoringModel(double[] weights, double bias, double learningRate, double margin)
        {
            _weights = weights;
            _bias = bias;
            _learningRate = learningRate;
            _margin = margin;
        }

        public double Score(double[] features)
        {
            if (features == null || features.Length != _weights.Length) return 0.0;
            double score = _bias;
            for (int i = 0; i < _weights.Length; i++)
                score += _weights[i] * features[i];
            return score;
        }

        public void UpdatePairwise(double[] teacherFeatures, double[] otherFeatures)
        {
            if (teacherFeatures == null || otherFeatures == null) return;
            if (teacherFeatures.Length != _weights.Length || otherFeatures.Length != _weights.Length) return;

            double teacherScore = Score(teacherFeatures);
            double otherScore = Score(otherFeatures);
            double loss = _margin - teacherScore + otherScore;

            if (loss <= 0) return;

            for (int i = 0; i < _weights.Length; i++)
            {
                _weights[i] += _learningRate * (teacherFeatures[i] - otherFeatures[i]);
            }
            _bias += _learningRate;
        }

        public string Serialize()
        {
            var sb = new StringBuilder();
            sb.Append(_weights.Length);
            sb.Append('|');
            sb.Append(_learningRate.ToString("R", CultureInfo.InvariantCulture));
            sb.Append('|');
            sb.Append(_margin.ToString("R", CultureInfo.InvariantCulture));
            sb.Append('|');
            sb.Append(_bias.ToString("R", CultureInfo.InvariantCulture));
            for (int i = 0; i < _weights.Length; i++)
            {
                sb.Append('|');
                sb.Append(_weights[i].ToString("R", CultureInfo.InvariantCulture));
            }
            return sb.ToString();
        }

        public static LinearScoringModel Deserialize(string data)
        {
            if (string.IsNullOrWhiteSpace(data)) return null;
            var parts = data.Split('|');
            if (parts.Length < 4) return null;

            int featureCount = int.Parse(parts[0], CultureInfo.InvariantCulture);
            double lr = double.Parse(parts[1], CultureInfo.InvariantCulture);
            double margin = double.Parse(parts[2], CultureInfo.InvariantCulture);
            double bias = double.Parse(parts[3], CultureInfo.InvariantCulture);

            var weights = new double[featureCount];
            for (int i = 0; i < featureCount && i + 4 < parts.Length; i++)
                weights[i] = double.Parse(parts[i + 4], CultureInfo.InvariantCulture);

            return new LinearScoringModel(weights, bias, lr, margin);
        }
    }
}
