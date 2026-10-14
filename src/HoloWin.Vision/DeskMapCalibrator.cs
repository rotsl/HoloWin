namespace HoloWin.Vision;

/// <summary>
/// Interactive desk surface calibration. Stores four desk corners in image
/// coordinates (pixel positions in the camera frame) and computes a homography
/// matrix that maps any image-space point to a normalized <c>[0,1] × [0,1]</c>
/// desk coordinate — independent of camera angle, perspective, or lens
/// distortion (within the planar-desk assumption).
///
/// <para>The four corners are stored in clockwise order starting at the
/// top-left of the desk as seen in the image:
/// <list type="number">
///   <item>0: top-left → desk (0, 0)</item>
///   <item>1: top-right → desk (1, 0)</item>
///   <item>2: bottom-right → desk (1, 1)</item>
///   <item>3: bottom-left → desk (0, 1)</item>
/// </list>
/// "Top" / "bottom" here refer to image-space, not physical desk orientation
/// — the convention just needs to be consistent across all four corners.</para>
///
/// <para>Pure managed code, no native dependencies. The homography is solved
/// with an 8×8 Gaussian elimination with partial pivoting; this is fast
/// enough to call interactively on every mouse move during calibration.</para>
/// </summary>
public sealed class DeskMapCalibrator
{
    private const int CornerSlots = 4;

    // Destination corners in desk space — a unit square. Index matches the
    // image corner index (see class docs).
    private static readonly double[] DeskDstX = { 0, 1, 1, 0 };
    private static readonly double[] DeskDstY = { 0, 0, 1, 1 };

    private readonly double?[] _cornerImgX = new double?[CornerSlots];
    private readonly double?[] _cornerImgY = new double?[CornerSlots];

    // Homography coefficients [h11, h12, h13, h21, h22, h23, h31, h32].
    // h33 is implicitly 1. Null when no valid homography is available.
    private double[]? _h;

    /// <summary>
    /// Number of desk corners currently set. A homography requires all four.
    /// </summary>
    public int CornerCount
    {
        get
        {
            int c = 0;
            for (int i = 0; i < CornerSlots; i++)
                if (_cornerImgX[i].HasValue) c++;
            return c;
        }
    }

    /// <summary>True when a valid homography is available (4 corners set, non-degenerate).</summary>
    public bool IsCalibrated => _h is not null;

    /// <summary>
    /// Sets a desk corner in image coordinates. Calling this with a new value
    /// for an existing corner replaces it and recomputes the homography.
    /// </summary>
    /// <param name="index">Corner index in [0, 3] (see class docs for ordering).</param>
    /// <param name="imgX">Pixel X coordinate of the desk corner in the camera frame.</param>
    /// <param name="imgY">Pixel Y coordinate of the desk corner in the camera frame.</param>
    public void SetCorner(int index, double imgX, double imgY)
    {
        if ((uint)index >= CornerSlots)
            throw new ArgumentOutOfRangeException(nameof(index), index, "Corner index must be in [0, 3].");
        _cornerImgX[index] = imgX;
        _cornerImgY[index] = imgY;
        RecomputeHomography();
    }

    /// <summary>
    /// Clears a single corner. If this drops the corner count below 4, the
    /// homography is invalidated and <see cref="MapImagePointToDesk"/> will
    /// return (0, 0) until calibration is restored.
    /// </summary>
    public void ClearCorner(int index)
    {
        if ((uint)index >= CornerSlots)
            throw new ArgumentOutOfRangeException(nameof(index), index, "Corner index must be in [0, 3].");
        _cornerImgX[index] = null;
        _cornerImgY[index] = null;
        RecomputeHomography();
    }

    /// <summary>Clears all four corners.</summary>
    public void ClearCorners()
    {
        for (int i = 0; i < CornerSlots; i++)
        {
            _cornerImgX[i] = null;
            _cornerImgY[i] = null;
        }
        _h = null;
    }

    /// <summary>
    /// Returns the image-space coordinate of a corner, or null if not set.
    /// </summary>
    public (double ImgX, double ImgY)? GetCorner(int index)
    {
        if ((uint)index >= CornerSlots)
            throw new ArgumentOutOfRangeException(nameof(index), index, "Corner index must be in [0, 3].");
        if (!_cornerImgX[index].HasValue || !_cornerImgY[index].HasValue) return null;
        return (_cornerImgX[index]!.Value, _cornerImgY[index]!.Value);
    }

    /// <summary>
    /// Maps an image-space point to a normalized desk coordinate. Returns
    /// (0, 0) when fewer than four corners are set, or when the homography
    /// would divide by ~zero (degenerate input).
    /// </summary>
    /// <param name="imgX">Pixel X coordinate in the camera frame.</param>
    /// <param name="imgY">Pixel Y coordinate in the camera frame.</param>
    /// <returns>Normalized desk coordinate in <c>[0, 1] × [0, 1]</c>.</returns>
    public (double DeskX, double DeskY) MapImagePointToDesk(double imgX, double imgY)
    {
        var h = _h;
        if (h is null) return (0, 0);
        double denom = h[6] * imgX + h[7] * imgY + 1.0;
        if (Math.Abs(denom) < 1e-12) return (0, 0);
        double u = h[0] * imgX + h[1] * imgY + h[2];
        double v = h[3] * imgX + h[4] * imgY + h[5];
        return (u / denom, v / denom);
    }

    /// <summary>
    /// Returns a copy of the 8 homography coefficients
    /// [h11, h12, h13, h21, h22, h23, h31, h32] (h33 implicit = 1), or null
    /// if the calibrator is not in a calibrated state.
    /// </summary>
    public double[]? GetHomographyMatrix() => _h is null ? null : (double[])_h.Clone();

    /// <summary>
    /// Recomputes the homography by solving the 8×8 linear system built from
    /// the four (image → desk) correspondences. If fewer than four corners
    /// are set, or the system is singular (e.g. three corners collinear),
    /// the homography is set to null.
    /// </summary>
    private void RecomputeHomography()
    {
        // Need all four corners.
        for (int i = 0; i < CornerSlots; i++)
        {
            if (!_cornerImgX[i].HasValue || !_cornerImgY[i].HasValue)
            {
                _h = null;
                return;
            }
        }

        // Build 8x8 system A * h = b, where h = [h11, h12, h13, h21, h22, h23, h31, h32]^T.
        // For each corner i with (xi, yi) -> (Xi, Yi):
        //   row 2i:   [xi, yi, 1, 0,  0,  0,  -Xi*xi, -Xi*yi] = Xi
        //   row 2i+1: [0,  0,  0,  xi, yi, 1, -Yi*xi, -Yi*yi] = Yi
        double[][] a = new double[8][];
        double[] b = new double[8];
        for (int i = 0; i < 8; i++) a[i] = new double[8];
        for (int i = 0; i < CornerSlots; i++)
        {
            double xi = _cornerImgX[i]!.Value;
            double yi = _cornerImgY[i]!.Value;
            double Xi = DeskDstX[i];
            double Yi = DeskDstY[i];
            int r0 = 2 * i;
            int r1 = 2 * i + 1;

            a[r0][0] = xi;
            a[r0][1] = yi;
            a[r0][2] = 1;
            a[r0][6] = -Xi * xi;
            a[r0][7] = -Xi * yi;
            b[r0] = Xi;

            a[r1][3] = xi;
            a[r1][4] = yi;
            a[r1][5] = 1;
            a[r1][6] = -Yi * xi;
            a[r1][7] = -Yi * yi;
            b[r1] = Yi;
        }

        if (!TrySolveLinearSystem(a, b, out var h) || h is null)
        {
            _h = null;
            return;
        }
        _h = h;
    }

    /// <summary>
    /// Solves an n×n linear system A * x = b via Gaussian elimination with
    /// partial pivoting. Returns false if the matrix is singular.
    /// </summary>
    private static bool TrySolveLinearSystem(double[][] a, double[] b, out double[]? x)
    {
        // Definite assignment: out parameter must be set on every return path.
        x = null;
        int n = b.Length;
        // Augment A with b as the last column.
        double[][] m = new double[n][];
        for (int i = 0; i < n; i++)
        {
            m[i] = new double[n + 1];
            Array.Copy(a[i], m[i], n);
            m[i][n] = b[i];
        }

        // Forward elimination with partial pivoting.
        for (int pivot = 0; pivot < n; pivot++)
        {
            int maxRow = pivot;
            double maxVal = Math.Abs(m[pivot][pivot]);
            for (int r = pivot + 1; r < n; r++)
            {
                double v = Math.Abs(m[r][pivot]);
                if (v > maxVal)
                {
                    maxVal = v;
                    maxRow = r;
                }
            }
            if (maxVal < 1e-12)
                return false; // singular matrix
            if (maxRow != pivot)
            {
                (m[pivot], m[maxRow]) = (m[maxRow], m[pivot]);
            }

            double pivotVal = m[pivot][pivot];
            for (int r = pivot + 1; r < n; r++)
            {
                double factor = m[r][pivot] / pivotVal;
                if (factor == 0) continue;
                for (int c = pivot; c <= n; c++)
                {
                    m[r][c] -= factor * m[pivot][c];
                }
            }
        }

        // Back substitution.
        double[] result = new double[n];
        for (int i = n - 1; i >= 0; i--)
        {
            double s = m[i][n];
            for (int j = i + 1; j < n; j++)
                s -= m[i][j] * result[j];
            double diag = m[i][i];
            if (Math.Abs(diag) < 1e-12)
                return false;
            result[i] = s / diag;
        }
        x = result;
        return true;
    }
}
