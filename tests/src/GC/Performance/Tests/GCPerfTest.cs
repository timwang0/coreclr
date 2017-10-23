using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using System.Threading;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace GCPerfTest
{
    sealed class Rand
    {
        /* Generate Random numbers
         */
        private int x = 0;

        public int getRand()
        {
            x = (314159269 * x + 278281) & 0x7FFFFFFF;
            return x;
        }

        // obtain random number in the range 0 .. r-1
        public int getRand(int r)
        {
            // require r >= 0
            int x = (int)(((long)getRand() * r) >> 31);
            return x;
        }

        public int getRand(int low, int high)
        {
            int p = getRand(high - low);
            return (low + p);
        }

        public double getFloat()
        {
            return (double)getRand() / (double)0x7FFFFFFF;
        }

    };

    class MemoryAlloc
    {
        private Rand rand;
        private object[] oldArr;
        private int threadIndex;
        public bool pauseMeasure = false;
        private bool printIterInfo = false;
        int sohLow = 100;
        int sohHigh = 4000;
        int lohLow = 100 * 1000;
        int lohHigh = 200 * 1000;
        int lohAllocIterval = 0;
        int lohAllocRatio = 0;
        Int64 totalAllocBytes = 0;
        int sohSurvInterval = 0;
        int lohSurvInterval = 0;
        int totalMinutesToRun = 0;
        public List<double> times = new List<double>(10);

        static StreamWriter sw;

        MemoryAlloc(int i, int _lohAllocRatio, Int64 _totalAllocBytes, int _totalMinutesToRun, int _sohSurvInterval, int _lohSurvInterval)
        {
            rand = new Rand();
            threadIndex = i;

            printIterInfo = true;
            pauseMeasure = true;

            lohAllocRatio = _lohAllocRatio;
            if (lohAllocRatio != 0)
            {
                // Note that each LOH object is about ~50x each SOH object, which is why we are * 50 here
                if (lohAllocRatio > 0 && lohAllocRatio < 1000)
                {
                    lohAllocIterval = 1000 * 50 / lohAllocRatio;
                }
                else
                    lohAllocIterval = 1;
            }
            totalAllocBytes = _totalAllocBytes;

            // default is we survive every 30th element for SOH...this is about 3%.
            sohSurvInterval = _sohSurvInterval;

            // default is we survive every 5th element for SOH...this is about 20%.
            lohSurvInterval = _lohSurvInterval;

            totalMinutesToRun = _totalMinutesToRun;
        }

        int GetAllocBytes(bool isLarge)
        {
            return (isLarge ? rand.getRand(lohLow, lohHigh) : rand.getRand(sohLow, sohHigh));
        }

        void TouchPage(byte[] b)
        {
            int size = b.Length;

            int pageSize = 4096;

            int numPages = size / pageSize;

            for (int i = 0; i < numPages; i++)
            {
                b[i * pageSize] = (byte)i;
            }
        }

        public void Init()
        {
            int numSOHElements = (int)(((double)totalAllocBytes * (1000.0 - (double)lohAllocRatio) / 1000.0) / (double)((sohLow + sohHigh) / 2));
            int numLOHElements = (int)(((double)totalAllocBytes * (double)lohAllocRatio / 1000.0) / (double)((lohLow + lohHigh) / 2));

            sw.WriteLine("Allocating {0} soh elements and {1} loh", numSOHElements, numLOHElements);

            int numElements = numSOHElements + numLOHElements;
            oldArr = new object[numElements];

            int sohAllocatedElements = 0;
            int lohAllocatedElements = 0;
            Int64 sohAllocatedBytes = 0;
            Int64 lohAllocatedBytes = 0;

            for (int i = 0; i < numElements; i++)
            {
                bool isLarge = false;
                if (lohAllocIterval != 0)
                {
                    isLarge = ((i % lohAllocIterval) == 0);
                }

                int allocBytes = GetAllocBytes(isLarge);
                oldArr[i] = new byte[allocBytes];

                if (isLarge)
                {
                    lohAllocatedBytes += allocBytes;
                    lohAllocatedElements++;
                }
                else
                {
                    sohAllocatedBytes += allocBytes;
                    sohAllocatedElements++;
                }
            }

            sw.WriteLine("T{0}: allocated {1}({2} bytes) on SOH, {3}({4} byte) on LOH",
                threadIndex,
                sohAllocatedElements, sohAllocatedBytes,
                lohAllocatedElements, lohAllocatedBytes);
        }

        public void TimeTest()
        {
            Int64 n = 0;

            Stopwatch stopwatch = new Stopwatch();
            Stopwatch stopwatchGlobal = new Stopwatch();
            stopwatchGlobal.Reset();
            stopwatchGlobal.Start();

            Int64 print_iter = (Int64)1500 * 1024 * 1024;

            Int64 sohAllocatedBytes = 0;
            Int64 sohSurvivedBytes = 0;
            Int64 sohAllocatedCount = 0;

            Int64 lohAllocatedBytes = 0;
            Int64 lohSurvivedBytes = 0;
            Int64 lohAllocatedCount = 0;

            while (true)
            {
                if (n % (1024 * 1024) == 0) //100 * 1024 *1024;
                {
                    long elapsedMSec = (long)stopwatchGlobal.Elapsed.TotalMilliseconds;
                    int elapsedMin = (int)(elapsedMSec / (long)1000 / 60);
                    Console.WriteLine("iter {0}, {1}s elapsed, {2}min", n, elapsedMSec / 1000, elapsedMin);
                    if (elapsedMin >= totalMinutesToRun)
                    {
                        break;
                    }
                }
                if (printIterInfo && ((n % print_iter) == 0))
                {
                    sw.WriteLine("T{0}: iter {1}: soh allocated {2}, survived {3} ({4}%), loh {5} {6}({7}%)",
                        threadIndex, n,
                        sohAllocatedBytes, sohSurvivedBytes, ((sohAllocatedBytes == 0) ? 0 : (int)((double)sohSurvivedBytes * 100.0 / (double)sohAllocatedBytes)),
                        lohAllocatedBytes, lohSurvivedBytes, ((lohAllocatedBytes == 0) ? 0 : (int)((double)lohSurvivedBytes * 100.0 / (double)lohAllocatedBytes)));

                    sw.WriteLine("T{0}: gen0: {1}, gen1: {2}, gen2: {3}, heap size {4}mb",
                        threadIndex,
                        GC.CollectionCount(0),
                        GC.CollectionCount(1),
                        GC.CollectionCount(2),
                        (GC.GetTotalMemory(false) / 1024 / 1024));
                }

                bool isLarge = false;
                if (lohAllocIterval != 0)
                {
                    isLarge = ((n % lohAllocIterval) == 0);
                }

                int allocBytes = GetAllocBytes(isLarge);

                if (isLarge && pauseMeasure)
                {
                    stopwatch.Reset();
                    stopwatch.Start();

                    //sw.WriteLine("soh {0} bytes", allocBytes);
                }

                byte[] b = new byte[allocBytes];
                TouchPage(b);

                if (isLarge && pauseMeasure)
                {
                    stopwatch.Stop();
                    times.Add(stopwatch.Elapsed.TotalMilliseconds);
                }

                //Thread.Sleep(1);

                bool shouldSurvive = false;

                if (isLarge)
                {
                    lohAllocatedBytes += allocBytes;
                    lohAllocatedCount++;
                    shouldSurvive = ((lohAllocatedCount % lohSurvInterval) == 0);
                }
                else
                {
                    sohAllocatedBytes += allocBytes;
                    sohAllocatedCount++;
                    shouldSurvive = ((sohAllocatedCount % sohSurvInterval) == 0);
                }

                if (shouldSurvive)
                {
                    if (isLarge)
                        lohSurvivedBytes += allocBytes;
                    else
                        sohSurvivedBytes += allocBytes;

                    oldArr[rand.getRand(oldArr.Length)] = b;
                }

                n++;
            }
        }

        void PrintPauses()
        {
            if (pauseMeasure)
            {
                sw.WriteLine("T{0} {1} entries in pause", threadIndex, times.Count);
                sw.Flush();

                times.Sort();
                //times.OrderByDescending(a => a);

                sw.WriteLine("===============STATS for thread {0}=================", threadIndex);

                int startIndex = ((times.Count < 10) ? 0 : (times.Count - 10));
                for (int i = startIndex; i < times.Count; i++)
                {
                    sw.WriteLine(times[i]);
                }

                sw.WriteLine("===============END STATS for thread {0}=================", threadIndex);
            }
        }

        [DllImport("psapi.dll")]
        public static extern bool EmptyWorkingSet(IntPtr hProcess);

        static void DoTest()
        {
            Process currentProcess = Process.GetCurrentProcess();
            int currentPid = currentProcess.Id;
            string logFileName = currentPid + "-output.txt";
            sw = new StreamWriter(logFileName);

            Console.WriteLine("Process {0} creating {1} threads, loh alloc ratio is {2}%, total alloc {3}MB, running for {4}mins, SOH surv every {5} elements, LOH {6}",
                currentPid, g_threadCount, g_lohAllocRatio, g_totalAllocBytesMB, g_totalMinutesToRun, g_sohSurvInterval, g_lohSurvInterval);

            sw.WriteLine("Process {0} creating {1} threads, loh alloc ratio is {2}%, total alloc {3}MB, running for {4}mins, SOH surv every {5} elements, LOH {6}",
                currentPid, g_threadCount, g_lohAllocRatio, g_totalAllocBytesMB, g_totalMinutesToRun, g_sohSurvInterval, g_lohSurvInterval);
            sw.Flush();

            MemoryAlloc[] t = new MemoryAlloc[g_threadCount];
            ThreadStart ts;
            Thread[] threads = new Thread[g_threadCount];
            Int64 totalAllocBytes = (Int64)g_totalAllocBytesMB * (Int64)1024 * (Int64)1024;
            Int64 allocPerThread = totalAllocBytes / g_threadCount;

            for (int i = 0; i < g_threadCount; i++)
            {
                t[i] = new MemoryAlloc(i, g_lohAllocRatio, allocPerThread, g_totalMinutesToRun, g_sohSurvInterval, g_lohSurvInterval);
                ts = new ThreadStart(t[i].TimeTest);
                threads[i] = new Thread(ts);
                t[i].Init();
            }

            sw.WriteLine("after init: heap size {0}, press any key to continue", GC.GetTotalMemory(false));
            //Console.ReadLine();

            long tStart, tEnd;
            tStart = Environment.TickCount;

            for (int i = 0; i < g_threadCount; i++)
            {
                threads[i].Start();
            }

            for (int i = 0; i < g_threadCount; i++)
            {
                threads[i].Join();
            }

            tEnd = Environment.TickCount;

            sw.WriteLine("took {0}ms", (tEnd - tStart));
            sw.Flush();

            for (int i = 0; i < g_threadCount; i++)
            {
                t[i].PrintPauses();
            }

            sw.Flush();
            sw.Close();
        }

        static int g_threadCount = 4;
        static int g_lohAllocRatio = 5;
        static int g_totalAllocBytesMB = 200;
        static int g_totalMinutesToRun = 1;
        static int g_sohSurvInterval = 10;
        static int g_lohSurvInterval = 5;

        public static void Main(String[] args)
        {
            if (args.Length > 0)
            {
                g_threadCount = Int32.Parse(args[0]);
                if (args.Length > 1)
                    g_lohAllocRatio = Int32.Parse(args[1]);
                if (args.Length > 2)
                    g_totalAllocBytesMB = Int32.Parse(args[2]);
                if (args.Length > 3)
                    g_totalMinutesToRun = Int32.Parse(args[3]);
                if (args.Length > 4)
                    g_sohSurvInterval = Int32.Parse(args[4]);
                if (args.Length > 5)
                    g_lohSurvInterval = Int32.Parse(args[5]);
            }

            DoTest();

            GC.Collect(2, GCCollectionMode.Forced, true);

            EmptyWorkingSet(Process.GetCurrentProcess().Handle);

            //Debugger.Break();
            //throw new System.ArgumentException("Just an opportunity for debugging", "test");
        }
    };

}
