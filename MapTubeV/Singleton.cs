using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

//NOT USED
namespace MapTubeV
{
    public class Singleton
    {
    }

    /// <summary>
    /// Generic singleton pattern. TODO: is this needed??????
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public class Singleton<T> where T : new()
    {
        Singleton() { }

        public static T Instance
        {
            get { return SingletonCreator.instance; }
        }

        class SingletonCreator
        {
            static SingletonCreator() { }

            internal static readonly T instance = new T();
        }
    }
}