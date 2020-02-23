using System.Collections.Generic;

namespace Instances
{
    public static class Extensions 
    {
        public static void DequeueMultiple<T>(this Queue<T> stack, int amount)
        {
            for (var i = 0; i < amount; i++) stack.Dequeue();
        }
    }
}