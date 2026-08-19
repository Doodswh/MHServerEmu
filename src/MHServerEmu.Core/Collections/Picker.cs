using MHServerEmu.Core.Memory;
using MHServerEmu.Core.System.Random;

namespace MHServerEmu.Core.Collections
{
    public sealed class PickerPool<T> : GenericPool<Picker<T>>
    {
        public static ObjectPoolHandle<Picker<T>> Get(GRandom random, out Picker<T> picker)
        {
            var handle = Get(out picker);
            picker.Initialize(random);
            return handle;
        }

        public static ObjectPoolHandle<Picker<T>> Get(Picker<T> other, out Picker<T> picker)
        {
            var handle = Get(out picker);
            picker.Initialize(other);
            return handle;
        }
    }

    public class Picker<T> : IPoolable
    {
        enum WeightMode
        {
            Invalid,
            Weighted,
            UnWeighted
        }

        private readonly List<WeightedElement> _elements = new();

        private GRandom _random;
        private WeightMode _weightMode;
        private int _weights;

        public Picker() { }

        public Picker(GRandom random)
        {
            Initialize(random);
        }

        public void Initialize(GRandom random)
        {
            _elements.Clear();
            _random = random;
            _weightMode = WeightMode.Invalid;
            _weights = 0;
        }

        public void Initialize(Picker<T> other)
        {
            _elements.AddRange(other._elements);

            // IMPORTANT: The copy needs to use the same instance of random as the original to preserve the RNG sequence,
            // otherwise PickValidItem() and PickWeightTryAll() will keep picking the same things.
            _random = other._random;

            _weightMode = other._weightMode;
            _weights = other._weights;
        }

        public void Add(T element)
        {
            if (_weightMode == WeightMode.Invalid)
                _weightMode = WeightMode.UnWeighted;

            if (_weightMode == WeightMode.UnWeighted)
            {
                _elements.Add(new WeightedElement(element, 1));
                _weights += 1;
            }
        }

        public void Add(T element, int weight)
        {
            if (_weightMode == WeightMode.Invalid)
                _weightMode = WeightMode.Weighted;

            if (_weightMode == WeightMode.Weighted && weight > 0)
            {
                _elements.Add(new WeightedElement(element, weight));
                _weights += weight;
            }
        }

        public bool Empty()
        {
            return _elements.Count == 0;
        }

        public int GetNumElements()
        {
            return _elements.Count;
        }

        public int GetRandomIndex()
        {
            if (_weightMode == WeightMode.UnWeighted)
                return GetRandomIndexUnweighted();
            else
                return GetRandomIndexWeighted();
        }

        public int GetRandomIndexUnweighted()
        {
            return _random.Next(0, _elements.Count);
        }

        public int GetRandomIndexWeighted()
        {
            int r = _random.Next(1, _weights + 1);
            int sum = 0;
            int index = 0;

            foreach (var element in _elements)
            {
                sum += element.Weight;
                if (sum >= r) break;
                index++;
            }

            return index;
        }

        public bool GetElementAt(int index, out T element)
        {
            element = default; 

            if (index >= 0 && index < _elements.Count)
            {
                element = _elements[index].Element;
                return true;
            }

            return false;
        }

        public T Pick()
        {
            if (Empty()) return default; 
            return _elements[GetRandomIndex()].Element;
        }

        public bool Pick(out T element)
        {
            if (Empty())
            {
                element = default;
                return false;
            }

            element = _elements[GetRandomIndex()].Element;
            return true;
        }

        public bool Pick(out T element, out int index)
        {
            if (Empty())
            {
                element = default;
                index = -1;
                return false;
            }

            index = GetRandomIndex();
            element = _elements[index].Element;

            return true;
        }

        public bool PickRemove(out T element)
        {           
            if (Empty())
            {   
                element = default;
                return false;
            }

            int index = GetRandomIndex();

            element = _elements[index].Element;
            _weights -= _elements[index].Weight;

            _elements[index] = _elements[_elements.Count - 1];
            _elements.RemoveAt(_elements.Count - 1);

            return true;
        }

        public bool RemoveIndex(int index)
        {
            if (Empty()) return false;

            if (index >= 0 && index < _elements.Count)
            {
                _weights -= _elements[index].Weight;
                _elements.RemoveAt(index);
                
                return true;
            }

            return false;
        }

        public void Clear()
        {
            _elements.Clear();
            _weights = 0;
        }

        public void ResetForPool()
        {
            _elements.Clear();
            _random = default;
            _weightMode = default;
            _weights = default;
        }

        private readonly struct WeightedElement(T element, int weight)
        {
            public T Element { get; } = element;
            public int Weight { get; } = weight;
        }
    }
}
