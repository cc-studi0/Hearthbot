using System;
using Xunit;
using BotMain.Learning;

namespace BotCore.Tests.Learning
{
    public class LinearScoringModelTests
    {
        [Fact]
        public void Score_InitialWeights_ReturnsZero()
        {
            var model = new LinearScoringModel(featureCount: 4);
            var features = new double[] { 1.0, 2.0, 3.0, 4.0 };
            Assert.Equal(0.0, model.Score(features));
        }

        [Fact]
        public void UpdatePairwise_TeacherGetsHigherScore()
        {
            var model = new LinearScoringModel(featureCount: 3, learningRate: 0.1);
            var teacherFeatures = new double[] { 1.0, 0.0, 0.0 };
            var otherFeatures = new double[] { 0.0, 1.0, 0.0 };

            for (int i = 0; i < 100; i++)
                model.UpdatePairwise(teacherFeatures, otherFeatures);

            Assert.True(model.Score(teacherFeatures) > model.Score(otherFeatures));
        }

        [Fact]
        public void SerializeDeserialize_PreservesWeights()
        {
            var model = new LinearScoringModel(featureCount: 3, learningRate: 0.1);
            var teacherFeatures = new double[] { 1.0, 0.5, 0.0 };
            var otherFeatures = new double[] { 0.0, 0.5, 1.0 };

            for (int i = 0; i < 50; i++)
                model.UpdatePairwise(teacherFeatures, otherFeatures);

            var serialized = model.Serialize();
            var restored = LinearScoringModel.Deserialize(serialized);

            Assert.Equal(model.Score(teacherFeatures), restored.Score(teacherFeatures), precision: 6);
        }

        [Fact]
        public void Score_NullFeatures_ReturnsZero()
        {
            var model = new LinearScoringModel(featureCount: 3);
            Assert.Equal(0.0, model.Score(null));
        }

        [Fact]
        public void Score_WrongLength_ReturnsZero()
        {
            var model = new LinearScoringModel(featureCount: 3);
            Assert.Equal(0.0, model.Score(new double[] { 1.0 }));
        }
    }
}
