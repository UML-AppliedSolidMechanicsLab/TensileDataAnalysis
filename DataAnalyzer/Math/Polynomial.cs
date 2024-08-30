/*
 * Created by SharpDevelop.
 * User: e15564
 * Date: 6/13/2007
 * Time: 12:31 PM
 * 
 * To change this template use Tools | Options | Coding | Edit Standard Headers.
 */

using System;


namespace DataAnalyzer.Math
{
	/// <summary>
	/// Description of Polynomial.
	/// </summary>
	public class Polynomial
	{
		public static double EvaluatePolynomial(double inX, double[,] inC){
    		int I;
    		int PolynomialOrder;
    		double evaluatePolynomial = 0;
    		PolynomialOrder = inC.GetUpperBound(0);
    		for (I = 0; I < PolynomialOrder+1; I++){
    			evaluatePolynomial = evaluatePolynomial + inC[I, 0] * System.Math.Pow(inX,I);
    		}
    		return evaluatePolynomial;
		}

        ///<summary>
        ///Fits the data to a polynomial of a given order
        /// </summary>
        /// <param name="inPolynomialOrder">can be any integer (best to keep it under 10, get bad stuff after).</param>
        /// <param name="inX">arrays containing the data points</param>
        /// <param name="inY">arrays containing the data points</param>
        /// <param name="Cout">contains polynomial coefficients</param>
        /// <param name="SEi">standard errors on coefficients</param>
        /// <param name="Rsquared">correlation coefficient</param>
        /// <param name="residualSumSquared">residuals and Residual Sum of Squares</param>
        /// <exception cref="AccessViolationException">If the order is higher than the amount of data</exception>
        public void PolynomialFit(int inPolynomialOrder, double[] inX, double[] inY,
                          out double[,] Cout, out double[] SEi,
                          out double Rsquared, out double residualSumSquared)
        {
            int I;
            int j;
            int k;
            long inCount = inX.Length;
            double residual;
            double Ybar = 0;
            double SEySquared;
            double VarYaboutMean;
            double SSyy = 0;      // Total Sum of Squares
            double[,] a = new double[inPolynomialOrder + 1, inPolynomialOrder + 1];
            double[,] b = new double[inPolynomialOrder + 1, 1];
            double[,] Aout = new double[inPolynomialOrder + 1, inPolynomialOrder + 1];

            // Initialize the out parameters
            Cout = new double[inPolynomialOrder + 1, 1];
            SEi = new double[inPolynomialOrder + 1];
            Rsquared = 0;
            residualSumSquared = 0;

            if (inX.Length <= inPolynomialOrder)
            {
                throw new AccessViolationException();
            }

            // Initialize coefficient matrices
            for (I = 0; I < inPolynomialOrder + 1; I++)
            {
                for (j = 0; j < inPolynomialOrder + 1; j++)
                {
                    a[I, j] = 0;
                }
                b[I, 0] = 0;
            }

            for (I = 0; I < inPolynomialOrder + 1; I++)
            {
                for (j = 0; j < inPolynomialOrder + 1; j++)
                {
                    for (k = 0; k < inCount; k++)
                    {
                        a[I, j] += System.Math.Pow(inX[k], (j + I));
                    }
                }
                for (k = 0; k < inCount; k++)
                {
                    b[I, 0] += inY[k] * System.Math.Pow(inX[k], (I));
                }
            }

            // Invert coefficient matrix
            MatrixMath mymath = new MatrixMath();
            Aout = mymath.InvertMatrix(a, Aout, inPolynomialOrder + 1, inPolynomialOrder + 1);

            // Multiply by input vector to get polynomial coefficients
            Cout = mymath.MatrixMultiply(Cout, Aout, b, inPolynomialOrder + 1,
                                         inPolynomialOrder + 1, inPolynomialOrder + 1, 1);

            // Calculate residuals and Residual Sum of Squares
            for (I = 0; I < inCount; I++)
            {
                residual = EvaluatePolynomial(inX[I], Cout) - inY[I];
                residualSumSquared += System.Math.Pow(residual, 2);
                Ybar += inY[I];
            }

            Ybar /= inCount;

            // Calculate standard errors on coefficients
            SEySquared = residualSumSquared / (inX.Length - (inPolynomialOrder + 1));
            for (I = 0; I < inPolynomialOrder + 1; I++)
            {
                double temp = System.Math.Abs(Aout[I, I]);
                SEi[I] = System.Math.Sqrt(SEySquared * temp);
            }

            // Calculate the total sum of squares
            for (I = 0; I < inCount; I++)
            {
                VarYaboutMean = inY[I] - Ybar;
                SSyy += System.Math.Pow(VarYaboutMean, 2);
            }

            // Check for edge case where SSyy is zero
            if (SSyy == 0)
            {
                Rsquared = 1.0;
            }
            else
            {
                Rsquared = 1 - residualSumSquared / SSyy;
            }
        }

    }
}
