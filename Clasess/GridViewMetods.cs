using System;
using System.Drawing;
using System.Windows.Forms;

namespace Jornal
{
    public class GridViewMetods
    {
        static public void ColorSelectedRows(DataGridView grid)
        {
            if (grid == null || grid.CurrentRow == null || grid.CurrentRow.Cells[14] == null || grid.CurrentRow.Cells[14].Value == null)
            {
                return;
            }

            if (Convert.ToInt32((grid.CurrentRow.Cells[14].Value)) == -1)
            {
                grid.CurrentRow.DefaultCellStyle.SelectionBackColor = Color.DarkCyan;
            }
            else
            {
                Color color = Color.FromArgb(Convert.ToInt32((grid.CurrentRow.Cells[14].Value)));
                int darkerRed = (int)(color.R * 0.6);
                int darkerGreen = (int)(color.G * 0.6);
                int darkerBlue = (int)(color.B * 0.6);
                grid.CurrentRow.DefaultCellStyle.SelectionBackColor = Color.FromArgb(darkerRed, darkerGreen, darkerBlue);
            }
        }
    }
}
