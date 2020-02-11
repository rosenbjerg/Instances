using System.Collections.Generic;

namespace Instances
{
    public static class Extensions 
    {
        public static void PopMultiple<T>(this Stack<T> stack, int amount)
        {
            for (var i = 0; i < amount; i++) stack.Pop();
        }
    }
}