using System;

namespace Trainer.Simulation.LifeSupport
{
    /// <summary>
    /// Ideal-gas conversion for ONE well-mixed cabin zone (declared simplification): p = (m / M) · R · T / V.
    /// Units at the boundary: kilograms, kilopascals, cubic metres, kelvin. Temperature and free gas volume are
    /// constant (from <see cref="CabinConfiguration"/>); total pressure and other gases are not modelled.
    /// </summary>
    public static class CabinGas
    {
        /// <summary>Molar gas constant, J/(mol·K) (CODATA exact value).</summary>
        public const double GasConstant = 8.314462618;
        /// <summary>Molar mass of O2, kg/mol.</summary>
        public const double OxygenMolarMass = 0.0319988;
        /// <summary>Molar mass of CO2, kg/mol.</summary>
        public const double CarbonDioxideMolarMass = 0.0440095;

        /// <summary>Gas mass (kg) of a species at <paramref name="partialPressureKilopascals"/> in the cabin.</summary>
        public static double MassKilograms(double partialPressureKilopascals, double molarMass, CabinConfiguration cabin)
        {
            if (cabin == null) throw new ArgumentNullException(nameof(cabin));
            return partialPressureKilopascals * 1000d * cabin.FreeGasVolumeCubicMeters * molarMass
                   / (GasConstant * cabin.GasTemperatureKelvin);
        }

        /// <summary>Partial pressure (kPa) of <paramref name="massKilograms"/> of a species in the cabin.</summary>
        public static double PartialPressureKilopascals(double massKilograms, double molarMass, CabinConfiguration cabin)
        {
            if (cabin == null) throw new ArgumentNullException(nameof(cabin));
            return massKilograms / molarMass * GasConstant * cabin.GasTemperatureKelvin
                   / cabin.FreeGasVolumeCubicMeters / 1000d;
        }
    }
}
