using System;
using NUnit.Framework;
using Unity.Collections;
using Unity.InferenceEngine;
using UnityEngine;

namespace VrChat.Testing
{
    [TestFixture]
    public class TensorReadbackTests
    {
        static readonly float[] kValues = { 1f, -2f, 3.5f, 4f };

        // GPU tensor data whose first `failures` downloads throw, as ComputeTensorData's does when its async
        // readback completed with an error: the request reads as done, and GetData throws.
        sealed class FailingReadbackData : ITensorData
        {
            readonly float[] _values;
            int _failures;

            public FailingReadbackData(float[] values, int failures)
            {
                _values = values;
                _failures = failures;
            }

            public int maxCapacity => _values.Length;
            public BackendType backendType => BackendType.GPUCompute;
            public void Upload<T>(NativeArray<T> data, int srcCount) where T : unmanaged { }
            public bool IsReadbackRequestDone() => true;
            public void ReadbackRequest() { }
            public void CompleteAllPendingOperations() { }
            public Awaitable<NativeArray<T>> DownloadAsync<T>(int dstCount) where T : unmanaged => throw new NotSupportedException();
            public void Dispose() { }

            public NativeArray<T> Download<T>(int dstCount) where T : unmanaged
            {
                if (_failures > 0)
                {
                    _failures--;
                    throw new InvalidOperationException("Cannot access the data as it is not available");
                }
                return new NativeArray<float>(_values, Allocator.Temp).Reinterpret<T>(sizeof(float));
            }
        }

        static Tensor<float> ReadbackTensor(int failures)
        {
            var tensor = new Tensor<float>(new TensorShape(kValues.Length));
            tensor.AdoptTensorData(new FailingReadbackData(kValues, failures));
            tensor.ReadbackRequest();
            return tensor;
        }

        [Test]
        public void Download_AsyncReadbackSucceeded_ReturnsTheTensorsData()
        {
            using var tensor = ReadbackTensor(failures: 0);

            var actual = TensorReadback.Download(tensor);

            Assert.That(actual, Is.EqualTo(kValues));
        }

        [Test]
        public void Download_AsyncReadbackFailed_ReturnsTheTensorsData()
        {
            using var tensor = ReadbackTensor(failures: 1);

            var actual = TensorReadback.Download(tensor);

            Assert.That(actual, Is.EqualTo(kValues));
        }

        [Test]
        public void Download_RetryFailsToo_ThrowsInvalidOperationException()
        {
            using var tensor = ReadbackTensor(failures: 2);

            Assert.That(() => TensorReadback.Download(tensor), Throws.TypeOf<InvalidOperationException>());
        }
    }
}
