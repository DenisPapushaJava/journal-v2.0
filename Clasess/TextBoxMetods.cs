using System.Linq;
using System.Windows.Forms;

namespace Jornal
{
    public class TextBoxMetods
    {
        static public void CheckTB(TextBox tbChek)
        {
           string str = tbChek.Text;

        string outS = string.Empty;
        bool comma = true;
            
            foreach (char ch in str)
                if (char.IsDigit(ch) | char.IsLetter(ch) || (ch == '.' && comma))
                {
                    outS += ch;
                    if (ch == '.')
                        comma = false;
                }

    tbChek.Text = outS;
            tbChek.SelectionStart = outS.Length;

            if (str.Contains(","))
            {
                string s = str.Replace(",", ".");
    tbChek.Clear();
                tbChek.AppendText(str.Replace(",", "."));

            }  
        }

        public static void CheckTBChar(TextBox tbChar, KeyPressEventArgs e)
        {
            char number = e.KeyChar;
            if (tbChar.Text == "")
            {
                e.Handled |= !char.IsDigit(number) && number != 8 || number == 48;
            }
            else
            {
                e.Handled |= !char.IsDigit(number) && number != 8 && number != 44 && number != 46;
            }
        }


        public static void ClearText(TextBox tb)
        {
            tb.Text = "";
            tb.Focus();
        }
        public static void ClearInfo(ToolStripStatusLabel tSSL)
        {
            tSSL.Text= string.Empty;
        }
        public static void AddTextInfo(StatusStrip stStrip, ToolStripStatusLabel tSSL, string info)
        {
           stStrip.Visible = true;
           tSSL.Text = info;
        }
    }
}
