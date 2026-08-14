/*
 * Created by SharpDevelop.
 * User: e46221
 * Date: 7/11/2007
 * Time: 11:23 AM
 * 
 * To change this template use Tools | Options | Coding | Edit Standard Headers.
 */

using System;
using System.Windows.Forms;
using System.Collections.Generic;
using DataAnalyzer;
using System.Xml;

namespace DataAnalyzer.Math
{
	
	public class Analyze
	{
		private string [] root;
		private int [] tranChan, axChan, rowStart, rowEnd, chanStrain, chanStress, dispflag;
		private double [] length, xSecArea;
		private double cutoff, interval;
		private string temperature;
		private int locPolyOrder, globPolyOrder, numberofFiles, numberofTranFiles;
		private int extrapPoints;
		private double extrapXCommon;
		private FileReader[] rawData;
		private Averager[] aveData;
		private Zeroer[] zeroData;
		private Zeroer[] zeroNUData;
		private double [,] combinedData;
		private double [,] combinedNUData;
		private Combination total;
		private Combination nu;

		private double [,] combinedDataExtrapolated;
		private double [,] combiedNuDataExtrapolated;
		private Combination totalExtrapolated;
		private Combination nuExtrapolated;
		
		/// <summary>
		/// takes in an inputArray where each column is the input data for each file scanned,
		/// with the 1st row being the root, 2nd the # of axial strain channels, 3rd the #
		/// of transverse strain channels, 4th the specimen length, and 5th the x-sec area.
		/// The last column is for the group inputs, with the 1st row being the cutoff strain,
		/// 2nd the local polynomial order, 3rd the global polynomial order, 4th the LOESS interval,
		/// 5th the temperature, 6th the # of files scanned in, 7th the # of those with transverse
		/// gauges, 8th the # of trailing points used for extrapolation, and 9th the common x value
		/// every curve is extrapolated out to.
		/// Also, takes in the offest array: 1st element is 0 if not an option
		/// </summary>
		/// <param name="individualInputsList"></param>
		/// <param name="groupInputsList"></param>
		/// <param name="offsetArray"></param>
		public Analyze(List<string[]> individualInputsList, string[] groupInputsList, double [] offsetArray){

			// numberofFiles = how many specimens were loaded; numberofTranFiles = how many of those
			// also have transverse strain gauges (needed to compute Poisson's ratio, "NU").
			numberofFiles = Convert.ToInt32(groupInputsList[5]);
			numberofTranFiles = Convert.ToInt32(groupInputsList[6]);

			int i,j, k, sum;
			// One slot per specimen for each stage of the pipeline: raw CSV data -> channel-averaged
			// data -> zeroed/cut/LOESS-smoothed data. zeroNUData is the transverse-strain equivalent
			// of zeroData, used only for specimens that have transverse gauges.
			rawData = new FileReader[numberofFiles];
			aveData = new Averager[numberofFiles];
			zeroData = new Zeroer[numberofFiles];
			zeroNUData = new Zeroer[numberofTranFiles];
			root = new string [numberofFiles];
			axChan = new int [numberofFiles];
			tranChan = new int [numberofFiles];
			length = new double [numberofFiles];
			xSecArea = new double [numberofFiles];
			rowStart = new int [numberofFiles];
			rowEnd = new int [numberofFiles];
			chanStrain = new int [numberofFiles];
			chanStress = new int [numberofFiles];
			dispflag = new int [numberofFiles];

			//split up the input array into seperate arrays and variables

			// Group-level settings that apply to every specimen in this run (set once from the
			// "group inputs" the user entered, not per-file).
			cutoff = Convert.ToDouble(groupInputsList[0]);       // strain value past which data is discarded
			locPolyOrder = Convert.ToInt32(groupInputsList[1]);  // polynomial order used for each local LOESS fit
			globPolyOrder = Convert.ToInt32(groupInputsList[2]); // polynomial order used for the single fit across all pooled data
			interval = Convert.ToDouble(groupInputsList[3]);     // LOESS window width (span) along the x-axis
			temperature = groupInputsList[4].ToString();
			extrapPoints = Convert.ToInt32(groupInputsList[7]);   // # of trailing points each curve's extrapolation fit is built from
			extrapXCommon = Convert.ToDouble(groupInputsList[8]); // x value every curve is extrapolated out to

			// Unpack each specimen's individual settings (file path/root, channel counts, gauge
			// geometry, which CSV rows/columns to read from, and whether axial data is raw
			// displacement vs. already-computed strain).
			for (i = 0; i < (numberofFiles); i++){
				root[i] = individualInputsList[i][0];
				axChan[i] = Convert.ToInt32(individualInputsList[i][1]);
				tranChan[i] = Convert.ToInt32(individualInputsList[i][2]);
				length[i] = Convert.ToDouble(individualInputsList[i][3]);
				xSecArea[i] = Convert.ToDouble(individualInputsList[i][4]);
				rowStart[i] = Convert.ToInt32(individualInputsList[i][5]);
				rowEnd[i] = Convert.ToInt32(individualInputsList[i][6]);
				chanStrain[i] = Convert.ToInt32(individualInputsList[i][7]);
				chanStress[i] = Convert.ToInt32(individualInputsList[i][8]);
				dispflag[i] = Convert.ToInt32(individualInputsList[i][9]);
			}


			// Per-specimen pipeline: for each loaded file, read + convert the raw CSV (FileReader),
			// average multiple axial/transverse channels down to one column each (Averager), then
			// zero, cut off at `cutoff` strain, and LOESS-smooth the stress/strain curve (Zeroer,
			// x=column 1 strain, y=column 0 stress). `sum` tallies the total number of surviving
			// (zeroed) data points across all specimens so combinedData can be sized correctly below.
			sum = 0;
			for (i = 0; i < (numberofFiles); i++){
				//Average all of the channels if there are more than one
				rawData[i] =new FileReader(root[i], axChan[i], tranChan[i], length[i], xSecArea[i],
				                           rowStart[i], rowEnd[i], chanStrain[i], chanStress[i], dispflag[i]);
				aveData[i] =new Averager(rawData[i].RawData, rawData[i].AxChan, rawData[i].TranChan);
				//Now find the offset to zero the data
				//offsetData = new Offset(total,offsetArray);
				zeroData[i] = new Zeroer(aveData[i].AveragedData, locPolyOrder, globPolyOrder, interval, cutoff, 1, 0, offsetArray);
				sum = sum + zeroData[i].ZeroedData.GetUpperBound(0)+1;
			}

			// Pool every specimen's zeroed stress/strain points into one big array (combinedData:
			// column 0 = stress, column 1 = strain), so they can be fit as a single combined curve
			// rather than specimen-by-specimen. Each specimen's block is individually sorted
			// ascending by strain (Zeroer.CutoffStrain sorts before returning ZeroedData), but this
			// concatenation does NOT re-sort across specimens, so combinedData as a whole is only
			// "sorted in per-specimen segments," not globally monotonic in strain.
			j = 0;
			combinedData = new double [sum, 2];
			for (i = 0; i < (numberofFiles); i++){
				for (k = 0; k < (zeroData[i].ZeroedData.GetUpperBound(0)+1); k++){
					combinedData[j,0] = zeroData[i].ZeroedData[k,0];
					combinedData[j,1] = zeroData[i].ZeroedData[k,1];
					j++;
				}
			}

			// `total` re-runs LOESS on the pooled data and fits one global polynomial through all
			// specimens combined, giving the material's overall stress-strain response (mean curve,
			// secant/tangent modulus, R^2, etc.). This is the object exposed as Analyze.Total below;
			// FileWriter5 writes it out to "TotalLoessDataE.csv", ResultsWindow prints its fit
			// coefficients/R^2 as the summary modulus results, and PlotMaker reads its MeanData/
			// Coefficients/FinalCout to draw the pooled stress-vs-strain and modulus-vs-strain plots.
			total = new Combination(combinedData, locPolyOrder, globPolyOrder, interval);

			//now do the same thing for transverse strain data (to find NU)
			if (numberofTranFiles != 0){
				// Same idea as above, but using transverse strain (x) vs. axial strain (y) instead of
				// stress vs. strain, only for specimens with transverse gauges (TranChan != 0). The
				// resulting fit slope is Poisson's ratio (NU) rather than a modulus.
				// zeroNUData only has numberofTranFiles slots, so pack the transverse specimens in
				// from 0 rather than storing them at their index in the full specimen list -- that
				// would leave holes, and would run off the end whenever a specimen with a
				// transverse gauge sits at an index >= numberofTranFiles.
				sum = 0;
				int t = 0;
				for (i = 0; i < (numberofFiles); i++){
					if (rawData[i].TranChan != 0){
						zeroNUData[t] = new Zeroer(aveData[i].AveragedData, locPolyOrder, globPolyOrder, interval, cutoff, 1, 2, offsetArray);
						sum = sum + zeroNUData[t].ZeroedData.GetUpperBound(0)+1;
						t++;
					}
				}

				j = 0;
				//with combinedNUData, first comes transverse [0], then axial [1]
				combinedNUData = new double [sum, 2];
				for (i = 0; i < (numberofTranFiles); i++){
					for (k = 0; k < (zeroNUData[i].ZeroedData.GetUpperBound(0)+1); k++){
						combinedNUData[j,0] = zeroNUData[i].ZeroedData[k,0];
						combinedNUData[j,1] = zeroNUData[i].ZeroedData[k,1];
						j++;
					}
				}
				// `nu` is the transverse/axial-strain counterpart of `total`: same pooled LOESS +
				// global-polynomial fit, but its slope represents Poisson's ratio. Exposed as
				// Analyze.NU; FileWriter6 writes it to "TotalLoessDataNU.csv", ResultsWindow reports
				// its coefficients as the NU summary, and PlotMaker uses its MeanData/Coefficients to
				// draw the transverse-vs-axial-strain plots.
				nu = new Combination(combinedNUData, locPolyOrder, globPolyOrder, interval);

			}
			//Now find the offset if it was checked in the input window
			//offsetData = new Offset(total,offsetArray);
			
			// NEW STUFF - Extrapolate sample curves based on last n points until a user-set value
			// Doing it here, not using the existing total Combination as the combinedData that
			// feeds into it smooshes the sample data together - the furthest forward we can go is ZeroedData
			// Both set by the user in the Group Inputs box; validated against the actual data below,
			// since the real limits aren't known until every specimen has been zeroed and cut off.
			// Restriction: Between 2 and length of shortest list, as the last n points are used to create a best-fit curve.
			int n = extrapPoints; 
			double xCommon = extrapXCommon; // Minimum is the largest final x across all specimens

			// Stress vs. strain: extend every specimen out to the common strain, pool the result,
			// and re-run the LOESS + global polynomial fit over the extended pool.
			combinedDataExtrapolated = ExtrapolateAndPool(zeroData, numberofFiles, n, xCommon);
			totalExtrapolated = new Combination(combinedDataExtrapolated, locPolyOrder, globPolyOrder, interval);

			//now do the same thing for transverse strain data (to find extrapolated NU)
			if (numberofTranFiles != 0){
				// Same call, but over the transverse zeroers: here x is still axial strain
				// (column 1) and y is transverse strain (column 0), so the pooled fit's slope is
				// the extrapolated Poisson's ratio rather than a modulus.
				combiedNuDataExtrapolated = ExtrapolateAndPool(zeroNUData, numberofTranFiles, n, xCommon);
				nuExtrapolated = new Combination(combiedNuDataExtrapolated, locPolyOrder, globPolyOrder, interval);
			}
		}

		/// <summary>
		/// Extends each zeroed curve past its last point out to xCommon, by fitting a polynomial of
		/// order globPolyOrder through that curve's last n points and stepping it forward by
		/// `interval`.  The original and extrapolated points are pooled into a single array using
		/// the same column convention as ZeroedData: column 0 = y, column 1 = x.
		/// </summary>
		/// <param name="curves">the zeroed curves to extend (stress/strain, or transverse/axial)</param>
		/// <param name="count">how many entries of `curves` to use</param>
		/// <param name="n">how many trailing points each curve's fit is built from</param>
		/// <param name="xCommon">the x value every curve is extended out to</param>
		/// <exception cref="ArgumentOutOfRangeException">if n or xCommon can't work for this data</exception>
		private double[,] ExtrapolateAndPool(Zeroer[] curves, int count, int n, double xCommon){
			int i, j, k;

			// ZeroedData rows are sorted ascending by x, so the last row holds each curve's largest
			// x.  xCommon has to reach at least the largest of those, or else some curve would have
			// to be truncated rather than extended.
			int shortestCurve = int.MaxValue;
			double furthestX = double.MinValue;
			for (i = 0; i < count; i++){
				int pointCount = curves[i].ZeroedData.GetUpperBound(0)+1;
				if (pointCount < shortestCurve)
					shortestCurve = pointCount;
				double lastX = curves[i].ZeroedData[pointCount-1,1];
				if (lastX > furthestX)
					furthestX = lastX;
			}
			// n also has to exceed globPolyOrder, or PolynomialFit has fewer points than coefficients
			if (n < 2 || n > shortestCurve || n <= globPolyOrder){
				//Reported to the user by MainForm's catch ladder
				throw new ArgumentOutOfRangeException("extrapPoints");
			}
			if (xCommon < furthestX){
				throw new ArgumentOutOfRangeException("extrapXCommon");
			}

			// Create dynamic-resizing copy of the zeroer instance's zeroedData [,] list [for each file]
			// We will add extrapolated points to this list-array, but first here we copy the existing zeroedData
			List<List<double>>[] zeroedPlusExtended = new List<List<double>>[count];
			for (i = 0; i < count; i++)
			{
				zeroedPlusExtended[i] = new List<List<double>>();
				for (j = 0; j < (curves[i].ZeroedData.GetUpperBound(0)+1); j++){
					// Column 0 is Y, column 1 is X; the list keeps that same order
					double y = curves[i].ZeroedData[j,0];
					double x = curves[i].ZeroedData[j,1];
					zeroedPlusExtended[i].Add(new List<double> {y, x});
				}
			}

			// To prepare for extrapolation, for each curve, create the best fit polynomial from the
			// last n average points on that curve.
			double[][] xEnd = new double[count][];
			double[][] yEnd = new double[count][];
			for (i = 0; i < count; i++)
			{
				int last = curves[i].ZeroedData.GetUpperBound(0);
				xEnd[i] = new double[n];
				yEnd[i] = new double[n];
				for (j = 0; j < n; j++){
					// Column 0 is Y, column 1 is X
					xEnd[i][j] = curves[i].ZeroedData[last - j,1];
					yEnd[i][j] = curves[i].ZeroedData[last - j,0];
				}
			}

			int sum = 0;
			double[][,] coeffs = new double[count][,];
			double[][] seiExtrapolated = new double[count][];
			double[] rSquaredExtrapolated = new double[count];
			double[] residualSumSquaredExtrapolated = new double[count];
			for (i = 0; i < count; i++){
				// Take last n points, create best fit line equation (or preset-polynomial-degree curve)
				Polynomial bestFit = new Polynomial();
				//The last n points were read newest-first, so sort back into ascending x order
				Array.Sort(xEnd[i], yEnd[i]);
				bestFit.PolynomialFit(globPolyOrder, xEnd[i], yEnd[i], out coeffs[i], out seiExtrapolated[i],
				                      out rSquaredExtrapolated[i], out residualSumSquaredExtrapolated[i]);

				// Then extend that line to xCommon, adding new points to that sample's average data.
				// Ideally the lines line up to each other so that the x values match.
				for(double x = xEnd[i][xEnd[i].GetUpperBound(0)] + interval; x <= xCommon; x += interval)
				{
					double y = Polynomial.EvaluatePolynomial(x, coeffs[i]);
					// Column 0 is Y, Column 1 is X, so the new row goes in as y, x
					zeroedPlusExtended[i].Add(new List<double> {y, x});
				}
				sum = sum + zeroedPlusExtended[i].Count;
			}

			// Pool every curve's points, original and extrapolated, into one array.  As with
			// combinedData, each curve's block is individually sorted ascending in x but this
			// concatenation does NOT re-sort across curves, so the result is only "sorted in
			// per-curve segments," not globally monotonic in x.
			j = 0;
			double [,] pooled = new double [sum, 2];
			for (i = 0; i < count; i++){
				for (k = 0; k < (zeroedPlusExtended[i].Count); k++){
					pooled[j,0] = zeroedPlusExtended[i][k][0];
					pooled[j,1] = zeroedPlusExtended[i][k][1];
					j++;
				}
			}
			return pooled;
		}
		
		//Accessors (so that I can get to them from MainForm
		public FileReader[] RawData{
			get{return rawData;}
		}
		public Averager[] AveData{
			get{return aveData;}
		}
		public Zeroer[] ZeroData{
			get{return zeroData;}
		}
		public double [,] CombinedData{
			get{return combinedData;}
		}
		public double [,] CombinedDataExtrapolated
		{
			get{return combinedDataExtrapolated;}
		}
		public Combination Total{
			get{return total;}
		}
		public Combination TotalExtrapolated
		{
			get{return totalExtrapolated;}
		}
		public Combination NU{
			get{return nu;}
		}
		public Combination NUExtrapolated{
			get{return nuExtrapolated;}
		}
		public double [,] CombinedNUData{
			get{return combinedNUData;}
		}
		public double [,] CombinedNUDataExtrapolated{
			get{return combiedNuDataExtrapolated;}
		}
		public Zeroer[] ZeroNUData{
			get{return zeroNUData;}
		}
		public double Cutoff{
			get{return cutoff;}
		}
		public double Interval{
			get{return interval;}
		}
		public double GlobPolyOrder{
			get{return globPolyOrder;}
		}
		public double LocPolyOrder{
			get{return locPolyOrder;}
		}
		public int NumberofTranFiles{
			get{return numberofTranFiles;}
		}
		public int ExtrapPoints{
			get{return extrapPoints;}
		}
		public double ExtrapXCommon{
			get{return extrapXCommon;}
		}
	}
}
