using System;

namespace SwanUnderwater
{
    /// <summary>
    /// One-time 5.6-second mono PCM synthesis for the hidden-treasure jackpot.
    /// Call during audio initialization, then reuse the resulting clip. No Unity,
    /// playback, streaming callback, or external audio dependency is required.
    /// </summary>
    public static class JackpotSoundSynthesis
    {
        public const double DurationSeconds = 5.6;
        const double TwoPi = Math.PI * 2.0;

        public static float[] Generate(int sampleRate)
        {
            if (sampleRate < 8000 || sampleRate > 192000)
                throw new ArgumentOutOfRangeException(nameof(sampleRate), "Expected 8000 to 192000 Hz.");
            var data = new float[(int)Math.Round(DurationSeconds * sampleRate)];
            var random = new Random(724601);

            // A quick ascending C-major flourish, resolving into an audible chord.
            // The last note starts before 0.5 seconds; its natural ring overlaps the coins.
            for (int i = 0; i < 8; i++)
            {
                double frequency;
                switch (i)
                {
                    case 0: frequency = 523.251; break;
                    case 1: frequency = 659.255; break;
                    case 2: frequency = 783.991; break;
                    case 3: frequency = 1046.502; break;
                    case 4: frequency = 1318.510; break;
                    case 5: frequency = 1567.982; break;
                    case 6: frequency = 1760.000; break;
                    default: frequency = 2093.005; break;
                }
                AddChime(data, sampleRate, i * .066, frequency, .11 - i * .003, .15);
            }
            AddChime(data, sampleRate, .49, 523.251, .105, .23);
            AddChime(data, sampleRate, .49, 659.255, .090, .21);
            AddChime(data, sampleRate, .49, 783.991, .080, .19);
            AddChime(data, sampleRate, .49, 1046.502, .070, .17);

            // Three increasingly dense/loud pours. Stratified irregular timings keep
            // this a cascade rather than a metronome or a few isolated bell notes.
            AddPour(data, sampleRate, random, .20, 1.65, 62, .72);
            AddPour(data, sampleRate, random, 1.53, 3.18, 90, .93);
            AddPour(data, sampleRate, random, 3.02, 4.80, 126, 1.10);
            AddCoin(data, sampleRate, random, 4.66, 1160, .038, .105);
            AddCoin(data, sampleRate, random, 4.73, 1480, .026, .115);
            AddCoin(data, sampleRate, random, 4.80, 925, .020, .120);

            // Remove accumulated DC and soften the very highest partials. These are
            // preparation-time filters; all oscillators also obey the Nyquist guard.
            double highPassMemory = Math.Exp(-TwoPi * 18.0 / sampleRate);
            double lowPassAmount = 1.0 - Math.Exp(-TwoPi * Math.Min(5700.0, sampleRate * .38) / sampleRate);
            double previousInput = 0.0, highPass = 0.0, lowPass = 0.0, peak = 0.0;
            for (int i = 0; i < data.Length; i++)
            {
                double input = data[i];
                highPass = input - previousInput + highPassMemory * highPass;
                previousInput = input;
                lowPass += lowPassAmount * (highPass - lowPass);
                double t = i / (double)sampleRate;
                double onset = Smooth01(t / .003);
                double release = Smooth01((data.Length - 1 - i) / (sampleRate * .65));
                data[i] = (float)(lowPass * onset * release);
                peak = Math.Max(peak, Math.Abs(data[i]));
            }
            // One constant gain preserves the three waves; no hard clipping or limiter
            // pumping. The extra margin below .75 also allows float rounding.
            double gain = peak > 0.0 ? .72 / peak : 1.0;
            for (int i = 0; i < data.Length; i++) data[i] = (float)(data[i] * gain);
            return data;
        }

        static void AddPour(float[] data, int sampleRate, Random random,
            double start, double end, int count, double strength)
        {
            for (int i = 0; i < count; i++)
            {
                double u = (i + .15 + random.NextDouble() * .70) / count;
                double time = start + (end - start) * u;
                double wave = .30 + .70 * Math.Pow(Math.Sin(Math.PI * u), .65);
                double frequency = 780 + Math.Pow(random.NextDouble(), .85) * 1760;
                double level = (.035 + random.NextDouble() * .045) * strength * wave;
                double decay = .026 + random.NextDouble() * .035;
                AddCoin(data, sampleRate, random, time, frequency, level, decay);
                // Smaller secondary impacts sell coins tumbling against one another.
                double bounceTime = time + .016 + random.NextDouble() * .030;
                if (bounceTime <= 4.80 && random.NextDouble() < .46)
                    AddCoin(data, sampleRate, random, bounceTime, frequency * (.88 + random.NextDouble() * .25),
                        level * .43, decay * .70);
            }
        }

        static void AddChime(float[] data, int sampleRate, double start, double frequency, double level, double decay)
        {
            AddPartial(data, sampleRate, start, frequency, level, decay, .005, 0);
            AddPartial(data, sampleRate, start, frequency * 2.001, level * .23, decay * .63, .004, 0);
            AddPartial(data, sampleRate, start, frequency * 3.002, level * .045, decay * .40, .003, 0);
        }

        static void AddCoin(float[] data, int sampleRate, Random random,
            double start, double frequency, double level, double decay)
        {
            // Inharmonic, independently decaying metal modes. No white-noise layer:
            // the dense small impacts create the rattle without a harsh noise burst.
            double phase = random.NextDouble() * TwoPi;
            AddPartial(data, sampleRate, start, frequency, level * .78, decay, .0018, phase);
            AddPartial(data, sampleRate, start, frequency * 1.483, level * .36, decay * .72, .0014, phase + .8);
            AddPartial(data, sampleRate, start, frequency * 2.137, level * .19, decay * .48, .0012, phase + 2.1);
            AddPartial(data, sampleRate, start, frequency * 2.791, level * .065, decay * .31, .0010, phase + 3.0);
        }

        static void AddPartial(float[] data, int sampleRate, double start, double frequency,
            double level, double decay, double attack, double phase)
        {
            if (frequency >= sampleRate * .43) return;
            if (frequency > 4600) level *= Math.Pow(4600 / frequency, 2);
            int first = (int)Math.Round(start * sampleRate);
            int length = Math.Min(data.Length - first, (int)Math.Ceiling(decay * 7.5 * sampleRate));
            if (first < 0 || length <= 0) return;
            double step = TwoPi * frequency / sampleRate;
            double stepSin = Math.Sin(step), stepCos = Math.Cos(step);
            double sine = Math.Sin(phase), cosine = Math.Cos(phase);
            double damping = Math.Exp(-1.0 / (sampleRate * decay));
            double attackSamples = Math.Max(1, attack * sampleRate);
            double tailSamples = Math.Max(1, .008 * sampleRate);
            // Recursive oscillators avoid millions of per-sample trig/exp calls.
            for (int i = 0; i < length; i++)
            {
                double envelope = Smooth01(i / attackSamples) * Smooth01((length - 1 - i) / tailSamples);
                data[first + i] += (float)(sine * level * envelope);
                double nextSine = sine * stepCos + cosine * stepSin;
                cosine = cosine * stepCos - sine * stepSin;
                sine = nextSine;
                level *= damping;
            }
        }

        static double Smooth01(double value)
        {
            value = Math.Max(0, Math.Min(1, value));
            return value * value * (3 - 2 * value);
        }
    }
}
