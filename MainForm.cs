using System;
using System.Collections.Generic;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using System.Data.OleDb;
using System.Configuration;
using System.Reflection;
using static Jornal.GridViewMetods;
using static Jornal.TextBoxMetods;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Threading;
using System.Globalization;
using System.Collections;
using Microsoft.Office.Interop.Excel;

namespace Jornal
{

    public partial class MainForm : Form
    {
        LoadForm loadForm = new LoadForm();
         public MainForm()
        {           
            loadForm.Show();            
            InitializeComponent();           
            //Grid_Order.DefaultCellStyle.SelectionBackColor = ColorTranslator.FromHtml("#9bc3c9");
          // LoadData();
            comboBox1.SelectedIndex = 0;      

        }

        private void Grid_OrderMouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
            {
                DataGridView.HitTestInfo hit = Grid_Order.HitTest(e.X, e.Y);
                if (hit.Type == DataGridViewHitTestType.Cell)
                {
                    Grid_Order.CurrentCell = Grid_Order[hit.ColumnIndex, hit.RowIndex];
                    contextMenuStrip1.Show(Grid_Order, e.X, e.Y);
                }
            }

        }


        private void ConnectAccess(string query)
        {
            ConnectionStringSettings conString;
            conString = ConfigurationManager.ConnectionStrings["pathdb"];
            try
            {
                OleDbConnection dbConn = new OleDbConnection(conString.ConnectionString);
                OleDbCommand dbCommand = new OleDbCommand(query, dbConn)
                {
                    Connection = dbConn,
                    CommandText = query
                };
                dbConn.Open();
                dbCommand.ExecuteNonQuery();
                dbConn.Close();
            }
            catch
            {
                MessageBox.Show("Не удалось подключиться к Базе Данных, программа будет закрыта", "Ошибка",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                Environment.Exit(0);
            }
        }
        private void ConnectDB(ConnectionStringSettings conStr, string qry)
        {
            OleDbConnection dbConn = new OleDbConnection(conStr.ConnectionString);
            OleDbCommand dbCommand = new OleDbCommand(qry, dbConn);
            OleDbDataAdapter da = new OleDbDataAdapter();
            System.Data.DataTable dt = new System.Data.DataTable();
            DataSet ds = new DataSet();
            dbCommand.Connection = dbConn;
            dbCommand.CommandText = qry;
            dbConn.Open();
            da.SelectCommand = dbCommand;
            da.Fill(ds);
            dt = ds.Tables[0];
            Grid_Order.DataSource = dt;
            Grid_Order.Columns["Код"].Visible = false;
            Grid_Order.Columns["Color"].Visible = false;
            Grid_Order.Columns["Коэффициент"].Visible = false;
            Grid_Order.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            dbConn.Close();
            statusStrip.Visible = false;
        }


        private void LoadData()
        {

            ConnectionStringSettings conString;
            conString = ConfigurationManager.ConnectionStrings["pathdb"];
            string query = "SELECT * FROM Orders";
            try
            {
               ConnectDB(conString, query);
            }
            catch
            {
                MessageBox.Show("Не удалось подключиться к Базе Данных, программа будет закрыта", "Ошибка",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                Environment.Exit(0);
            }
           
           
            
        }

        private void LoadDataSortDay()
        {
            checkBox_Finished.Checked = false;
            checkBox_Sent.Checked = false;
            checkBox_InWork.Checked = false;
           

            ConnectionStringSettings conString;
            conString = ConfigurationManager.ConnectionStrings["pathdb"];
            string query = "SELECT * FROM Orders WHERE Дата =  Date()";
            OleDbConnection dbConn = new OleDbConnection(conString.ConnectionString);
            try
            {
                ConnectDB(conString, query);
            }
            catch
            {
                MessageBox.Show("Не удалось подключиться к Базе Данных, программа будет закрыта", "Ошибка",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                Environment.Exit(0);
            }
        }

        private void SortChoiceDate()
        {
            checkBox_DateDay.Checked = false;
            checkBox_Finished.Checked = false;
            checkBox_Sent.Checked = false;
          

            ConnectionStringSettings conString;
            conString = ConfigurationManager.ConnectionStrings["pathdb"];
            
            DateTime selectDate = dateTimePicker_DateSort.Value;
            string sDFormat ="#" + selectDate.ToString("yyyy-MM-dd") + "#";
            string query = "SELECT * FROM Orders WHERE Дата =" + sDFormat;
            OleDbConnection dbConn = new OleDbConnection(conString.ConnectionString);
            try
            {
                ConnectDB(conString, query);
            }
            catch
            {
                MessageBox.Show("Не удалось подключиться к Базе Данных, программа будет закрыта", "Ошибка",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                Environment.Exit(0);
            }

            if (Grid_Order.Rows.Count == 0)
            {
                checkBox_SortChoiceDayON.Checked = false;
                MessageBox.Show("Заявки за " + dateTimePicker_DateSort.Value.ToShortDateString() + " не найдены!",
                    "Поиск по дате...", MessageBoxButtons.OK, MessageBoxIcon.Information);
                statusStrip.Visible = true;
                toolStripStatusLabel_Info.Text = "Заявки за " + dateTimePicker_DateSort.Value.ToShortDateString() + " не найдены!";
            }
        }

        private void LoadInWork()
        {           
            checkBox_DateDay.Checked = false;
            checkBox_Finished.Checked = false;
            checkBox_Sent.Checked = false;

            if (checkBox_InWork.Checked)
            {
                ConnectionStringSettings conString;
                conString = ConfigurationManager.ConnectionStrings["pathdb"];
                string query = "SELECT * FROM Orders WHERE Color = -882036";
                OleDbConnection dbConn = new OleDbConnection(conString.ConnectionString);
                try
                {
                    ConnectDB(conString, query);
                }
                catch
                {
                    MessageBox.Show("Не удалось подключиться к Базе Данных, программа будет закрыта", "Ошибка",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                    Environment.Exit(0);
                }
            }
        }

        private void LoadFinished()
        {
            
            checkBox_DateDay.Checked = false;
            checkBox_InWork.Checked = false;
            checkBox_Sent.Checked = false;
           
           
            if (checkBox_Finished.Checked)
            {               
                ConnectionStringSettings conString;
                conString = ConfigurationManager.ConnectionStrings["pathdb"];
                string query = "SELECT * FROM Orders WHERE Color = -10238087";
                OleDbConnection dbConn = new OleDbConnection(conString.ConnectionString);
                try
                {
                    ConnectDB(conString, query);
                }
                catch
                {
                    MessageBox.Show("Не удалось подключиться к Базе Данных, программа будет закрыта", "Ошибка",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                    Environment.Exit(0);
                }
            }
        }

        private void LoadSented()
        {
            checkBox_DateDay.Checked = false;
            checkBox_InWork.Checked = false;
            checkBox_Finished.Checked = false;

            if (checkBox_Sent.Checked)
            {
                ConnectionStringSettings conString;
                conString = ConfigurationManager.ConnectionStrings["pathdb"];
                string query = "SELECT * FROM Orders WHERE Color = -8154114";
                OleDbConnection dbConn = new OleDbConnection(conString.ConnectionString);
                try
                {
                    ConnectDB(conString, query);
                }
                catch
                {
                    MessageBox.Show("Не удалось подключиться к Базе Данных, программа будет закрыта", "Ошибка",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                    Environment.Exit(0);
                }
            }
        }

        private void RefrechColor()
        {
            for (int i = 0; i <= Grid_Order.Rows.Count - 1; i++)
            {
                int color = ColorFromDB(i);
                if (color != -1)
                {
                    Grid_Order.Rows[i].DefaultCellStyle.BackColor = Color.FromArgb(color);
                    Grid_Order.Rows[i].DefaultCellStyle.ForeColor = Color.Black;
                }

                if (color == -1)
                {
                    Grid_Order.Rows[i].DefaultCellStyle.ForeColor = Color.Black;
                }
            }
        }

        private void UpdateDataGrid()
        {
            
            if (checkBox_DateDay.Checked)
            {
                LoadDataSortDay();
            }
            else if (checkBox_InWork.Checked)
            {
                LoadInWork();
            }
            else if (checkBox_Finished.Checked)
            {
                LoadFinished();
            }
            else if (checkBox_Sent.Checked)
            {
                LoadSented();
            }
            else if (checkBox_SortChoiceDayON.Checked)
            {
                SortChoiceDate();
            }
            else
                LoadData();
            ClearTextResult();           
        }

        private void AddBLCB_In_DB()
        {
            if (Grid_Order.Rows.Count != 0)
            {
                Add_BLCB addBLCBDB = new Add_BLCB();

                for (int i = Grid_Order.SelectedRows.Count - 1; i >= 0; i--)
                {
                    if (!numberSelectedRows.Contains(Grid_Order.SelectedRows[i].Index))
                    {
                        numberSelectedRows.Add(Grid_Order.SelectedRows[i].Index);

                    }
                }

                string NumberBLCB, NameManager, NumberSpec;
                NumberSpec = string.Empty;
                int key = Convert.ToInt32(Grid_Order.CurrentRow.Cells[0].Value);
                NameManager = Convert.ToString(Grid_Order.CurrentRow.Cells[2].Value);
                NumberBLCB = Convert.ToString(Grid_Order.CurrentRow.Cells[13].Value);
                foreach (int i in numberSelectedRows)
                {
                    NumberSpec += $"     Спецификация№   {Convert.ToString(Grid_Order.Rows[Grid_Order.Rows[i].Index].Cells[3].Value)}\n";

                }
                addBLCBDB.NumberBLCB = NumberBLCB;
                addBLCBDB.LabelInfoBLCB = "Добавить к заявке менеджера  " + NameManager + "\n" + "\t" + "\t" + NumberSpec;
                addBLCBDB.ShowDialog();

                if (addBLCBDB.DialogResult == DialogResult.OK)
                {
                    NumberBLCB = addBLCBDB.NumberBLCB;
                    foreach (int i in numberSelectedRows)
                    {
                        key = Convert.ToInt32(Grid_Order.Rows[Grid_Order.Rows[i].Index].Cells[0].Value);
                        try
                        {
                            string query = "UPDATE Orders set БЛЦБ='" + NumberBLCB + "' WHERE Код = " + key;
                            ConnectAccess(query);
                        }
                        catch
                        {
                            MessageBox.Show("Номер БЛЦБ не добавлен!", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        }

                    }
                    numberSelectedRows.Clear();
                    UpdateDataGrid();
                }
                numberSelectedRows.Clear();

            }
        }

        private void ColorRow(int color, int key)
        {
            Grid_Order.Rows[key].DefaultCellStyle.BackColor = Color.FromArgb(color);
        }

        private void ColorInDB(int color)
        {
            if (Grid_Order.Rows.Count != 0)
            {
                if (Grid_Order.SelectedRows.Count > 1)
                {
                    string res;
                    var selectRow = Grid_Order.SelectedRows.Cast<DataGridViewRow>()
                        .Select(row => row.Cells["Код"].Value)
                        .OrderBy(id => id);
                    res = string.Join(",", selectRow);
                    string[] stackId = Regex.Split(res, ",");
                    int[] intStackId = new int[stackId.Length];
                    for (int i = 0; i < stackId.Length; i++)
                    {
                        intStackId[i] = Convert.ToInt32(stackId[i]);
                    }

                    foreach (var key in intStackId)
                    {
                        ConnectionStringSettings conString;
                        conString = ConfigurationManager.ConnectionStrings["pathdb"];
                        string getKey = "SELECT Код FROM Orders";

                        OleDbConnection dbConn = new OleDbConnection(conString.ConnectionString);
                        OleDbCommand dbCommand = new OleDbCommand(getKey, dbConn);
                        dbConn.Open();
                        OleDbDataReader dbReader = dbCommand.ExecuteReader();
                        dbReader.Read();

                        string query = "UPDATE Orders set Color='" + color + "' WHERE Код = " + key;
                        ConnectAccess(query);
                        dbConn.Close();
                    }
                }
                else
                {
                    int key = Convert.ToInt32(Grid_Order.CurrentRow.Cells[0].Value);
                    ConnectionStringSettings conString;
                    conString = ConfigurationManager.ConnectionStrings["pathdb"];
                    string getKey = "SELECT Код FROM Orders";

                    OleDbConnection dbConn = new OleDbConnection(conString.ConnectionString);
                    OleDbCommand dbCommand = new OleDbCommand(getKey, dbConn);
                    dbConn.Open();
                    OleDbDataReader dbReader = dbCommand.ExecuteReader();
                    dbReader.Read();

                    string query = "UPDATE Orders set Color='" + color + "' WHERE Код = " + key;
                    ConnectAccess(query);
                    dbConn.Close();
                }
            }
        }

        private int ColorFromDB(int key)
        {
            if ((Grid_Order.Rows[key].Cells[14].Value) != DBNull.Value)
            {
                int color = Convert.ToInt32(Grid_Order.Rows[key].Cells[14].Value);
                return color;
            }
            else
            {
                return -1;
            }
        }

        private void NewOrd()
        {
            int myColor;
            bool isColor = int.TryParse(ConfigurationManager.AppSettings["TechnologColor"], out myColor);
            if (!isColor) myColor = -1;
            
            ClearTextResult();
            Form_NewOrder newOrder = new Form_NewOrder();
            string data, manager, numberOfSpec, client, cut, steelGrade, sheetCount, thStr, weight, size, doubleSeparator;
            int lenghtCut;
            double thickness, priceResult, kF;
            newOrder.ShowDialog();
            CultureInfo culture = CultureInfo.CurrentCulture;
            doubleSeparator = culture.NumberFormat.NumberDecimalSeparator;            
            if (newOrder.DialogResult == DialogResult.OK)
            {
                //String
                data = newOrder.tBDate;              
                manager = newOrder.textBox_Manager.Text;
                numberOfSpec = newOrder.textBox_NumSpec.Text;
                client = newOrder.textBox_Client.Text;
                cut = newOrder.comboBox_Cut.Text;
                steelGrade = newOrder.comboBox_SteelGreed.Text;
                sheetCount = newOrder.textBox_SheetCount.Text;
                size = newOrder.comboBox_Size.Text;

                //Integer
                lenghtCut = Convert.ToInt32(newOrder.textBox_LengthCut.Text);
                //Doudle
                //thickness = newOrder.comboBox_Thickness.Text;
                thickness = Double.Parse(newOrder.comboBox_Thickness.Text.Replace('.', ','));
                thStr = newOrder.comboBox_Thickness.Text.Replace('.', ',');
                if (doubleSeparator == ",") {
                    kF = Double.Parse(newOrder.textBox_Kf.Text.Replace('.', ','));
                }
                else
                {
                    kF = Double.Parse(newOrder.textBox_Kf.Text.Replace(',', '.'));
                }
                //weight = double.Parse(newOrder.textBox_Weight.Text.Replace('.', ','));
                weight = newOrder.textBox_Weight.Text;
                bool flagCut = true;


                if (newOrder.comboBox_Cut.Text == "Лазер" & lenghtCut < 500 & thickness <= 20)
                {
                    string queryPrice = "SELECT * FROM PriceLL500";

                    ConnectionStringSettings conString;
                    conString = ConfigurationManager.ConnectionStrings["pathdb"];

                    string price;

                    OleDbConnection dbConn = new OleDbConnection(conString.ConnectionString);
                    OleDbCommand dbCommand = new OleDbCommand(queryPrice, dbConn);
                    dbConn.Open();
                    OleDbDataReader dbReader = dbCommand.ExecuteReader();
                    dbReader.Read();
                    price = Convert.ToString(dbReader[thStr]);
                    priceResult = Math.Round(Convert.ToDouble(price) * lenghtCut * kF);
                    string kFDetPunDB = $"{kF}|{newOrder.cntDetPun}";

                    string query =
                        "INSERT INTO Orders (Дата, Менеджер, Спецификация, Заказчик, Вид_резки, Марка_стали, Толщина, Размер_листа, Ход, Вес, Кол_во_листов, Стоимость, Color, Коэффициент) " +
                        "VALUES('" + data + "','" + manager + "','" + numberOfSpec + "', '" + client + "', '" + cut +
                        "', '" + steelGrade + "', '" + thickness +
                        "', '" + size + "', '" + lenghtCut + "', '" + weight + "', '" + sheetCount + "','" +
                        priceResult + "','" + myColor + "','" + kFDetPunDB + "'  )";
                    ConnectAccess(query);
                    dbConn.Close();
                    flagCut = false;
                }
                else if (newOrder.comboBox_Cut.Text == "Лазер" & lenghtCut >= 500 & thickness <= 20)
                {
                    string queryPrice = "SELECT * FROM PriceLM500";

                    ConnectionStringSettings conString;
                    conString = ConfigurationManager.ConnectionStrings["pathdb"];

                    string price;

                    OleDbConnection dbConn = new OleDbConnection(conString.ConnectionString);
                    OleDbCommand dbCommand = new OleDbCommand(queryPrice, dbConn);
                    dbConn.Open();
                    OleDbDataReader dbReader = dbCommand.ExecuteReader();
                    dbReader.Read();
                    price = Convert.ToString(dbReader[thStr]);
                    priceResult = Math.Round(Convert.ToDouble(price) * lenghtCut * kF);
                    string kFDetPunDB = $"{kF}|{newOrder.cntDetPun}";

                    string query =
                        "INSERT INTO Orders (Дата, Менеджер, Спецификация, Заказчик, Вид_резки, Марка_стали, Толщина, Размер_листа, Ход, Вес, Кол_во_листов, Стоимость, Color, Коэффициент) " +
                        "VALUES('" + data + "','" + manager + "','" + numberOfSpec + "', '" + client + "', '" + cut +
                        "', '" + steelGrade + "', '" + thickness +
                        "', '" + size + "', '" + lenghtCut + "', '" + weight + "', '" + sheetCount + "','" +
                        priceResult + "','" + myColor + "','" + kFDetPunDB + "'  )";

                    ConnectAccess(query);
                    dbConn.Close();
                    flagCut = false;
                }

                if (newOrder.comboBox_Cut.Text == "Плазма" & thickness >= 2 & thickness <= 32)
                {
                    string queryPrice = "SELECT * FROM PriceP";
                    ConnectionStringSettings conString;
                    conString = ConfigurationManager.ConnectionStrings["pathdb"];

                    string price;

                    OleDbConnection dbConn = new OleDbConnection(conString.ConnectionString);
                    OleDbCommand dbCommand = new OleDbCommand(queryPrice, dbConn);
                    dbConn.Open();
                    OleDbDataReader dbReader = dbCommand.ExecuteReader();
                    dbReader.Read();
                    price = Convert.ToString(dbReader[thStr]);
                    priceResult = Math.Round(Convert.ToDouble(price) * lenghtCut * kF);
                    string kFDetPunDB = $"{kF}|{newOrder.cntDetPun}";

                    string query =
                       "INSERT INTO Orders (Дата, Менеджер, Спецификация, Заказчик, Вид_резки, Марка_стали, Толщина, Размер_листа, Ход, Вес, Кол_во_листов, Стоимость, Color, Коэффициент) " +
                        "VALUES('" + data + "','" + manager + "','" + numberOfSpec + "', '" + client + "', '" + cut +
                        "', '" + steelGrade + "', '" + thickness +
                        "', '" + size + "', '" + lenghtCut + "', '" + weight + "', '" + sheetCount + "','" +
                        priceResult + "','" + myColor + "','" + kFDetPunDB + "'  )";
                    ConnectAccess(query);
                    dbConn.Close();
                    flagCut = false;
                }

                if (newOrder.comboBox_Cut.Text == "Газо-кислородная" & thickness >= 16 & thickness <= 90)
                {
                    string queryPrice = "SELECT * FROM PriceG";
                    ConnectionStringSettings conString;
                    conString = ConfigurationManager.ConnectionStrings["pathdb"];

                    string price;

                    OleDbConnection dbConn = new OleDbConnection(conString.ConnectionString);
                    OleDbCommand dbCommand = new OleDbCommand(queryPrice, dbConn);
                    dbConn.Open();
                    OleDbDataReader dbReader = dbCommand.ExecuteReader();
                    dbReader.Read();
                    price = Convert.ToString(dbReader[thStr]);
                    priceResult = Math.Round(Convert.ToDouble(price) * lenghtCut * kF);
                    string kFDetPunDB = $"{kF}|{newOrder.cntDetPun}";

                    string query =
                        "INSERT INTO Orders (Дата, Менеджер, Спецификация, Заказчик, Вид_резки, Марка_стали, Толщина, Размер_листа, Ход, Вес, Кол_во_листов, Стоимость, Color, Коэффициент) " +
                        "VALUES('" + data + "','" + manager + "','" + numberOfSpec + "', '" + client + "', '" + cut +
                        "', '" + steelGrade + "', '" + thickness +
                        "', '" + size + "', '" + lenghtCut + "', '" + weight + "', '" + sheetCount + "','" +
                        priceResult + "','" + myColor + "','" + kFDetPunDB + "'  )";

                    ConnectAccess(query);
                    dbConn.Close();
                    flagCut = false;
                }

                if (flagCut)
                {
                    MessageBox.Show("Не подходящие параметры для выбранного инструмента", "Внимание...",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    NewOrd();
                }

                UpdateDataGrid();
            }
        }

        private void Delete()
        {
            {
                //Кнопка удалить___________________________________________________
                if (Grid_Order.Rows.Count != 0)
                {
                    int key;
                    string managerName;
                    key = Convert.ToInt32(Grid_Order.CurrentRow.Cells[0].Value);
                    managerName = Convert.ToString(Grid_Order.CurrentRow.Cells[2].Value);
                    DialogResult resDel =
                        MessageBox.Show("Вы действительно хотите удалить заявку менеджера" + " " + managerName + "?",
                            "Удаление...", MessageBoxButtons.OKCancel, MessageBoxIcon.Question);

                    if (resDel == DialogResult.OK)
                    {
                        string query = "DELETE FROM Orders WHERE Код=" + key;
                        ConnectAccess(query);
                        UpdateDataGrid();
                    }
                }
            }
        }

        private void Edit()
        {
            if (Grid_Order.Rows.Count != 0)
            {
                Form_NewOrder newOrder = new Form_NewOrder();
                string data, manager, numberOfSpec, client, cut, steelGrade, sheetCount, thStr, weight, size, kFStr, cntDet, cntPun, doubleSeparator;
                int lenghtCut, key;
                double thickness, priceResult, kF;
                string[] kFDetPun;
                CultureInfo culture = CultureInfo.CurrentCulture;
                doubleSeparator = culture.NumberFormat.NumberDecimalSeparator;
                //Автозаполнение полей формы из GridView
                //newOrder.dateTimePicker_Order.Text = Convert.ToString(Grid_Order.CurrentRow.Cells[1].Value);
                newOrder.tBDate = (Convert.ToString((Grid_Order.CurrentRow.Cells[1].Value)).TrimEnd('0', ':', ' '));
                newOrder.tBManager = Convert.ToString(Grid_Order.CurrentRow.Cells[2].Value);
                newOrder.tBNSpec = Convert.ToString(Grid_Order.CurrentRow.Cells[3].Value);
                newOrder.tBClient = Convert.ToString(Grid_Order.CurrentRow.Cells[4].Value);
                newOrder.cBCut = Convert.ToString(Grid_Order.CurrentRow.Cells[5].Value);
                newOrder.cBSteelGrade = Convert.ToString(Grid_Order.CurrentRow.Cells[6].Value);
                newOrder.cBThickness = (Convert.ToString(Grid_Order.CurrentRow.Cells[7].Value).Replace('.', ','));
                newOrder.cBSize = Convert.ToString(Grid_Order.CurrentRow.Cells[8].Value);
                newOrder.tBLenCut = Convert.ToString(Grid_Order.CurrentRow.Cells[9].Value);
                newOrder.tBWeight = Convert.ToString(Grid_Order.CurrentRow.Cells[10].Value);
                newOrder.tBSheetCount = Convert.ToString(Grid_Order.CurrentRow.Cells[11].Value);
                kFDetPun = Convert.ToString(Grid_Order.CurrentRow.Cells[15].Value).Split('|');

                if (kFDetPun.Length == 3)
                {
                    kFStr = kFDetPun[0];
                    cntDet = kFDetPun[1];
                    cntPun = kFDetPun[2];
                    newOrder.tBKf = kFStr;
                    newOrder.LB_Details = cntDet;
                    newOrder.LB_Punchs = cntPun;
                }
                else
                {
                    newOrder.tBKf = "1.0";
                    newOrder.LB_Details = "?";
                    newOrder.LB_Punchs = "?";

                }
                bool flagCut = true;
                ClearTextResult();
                key = Convert.ToInt32(Grid_Order.CurrentRow.Cells[0].Value);

                newOrder.ShowDialog();
                if (newOrder.DialogResult == DialogResult.OK)
                {
                    //String
                    data = newOrder.tBDate;
                    manager = newOrder.textBox_Manager.Text;
                    numberOfSpec = newOrder.textBox_NumSpec.Text;
                    client = newOrder.textBox_Client.Text;
                    cut = newOrder.comboBox_Cut.Text;
                    steelGrade = newOrder.comboBox_SteelGreed.Text;
                    sheetCount = newOrder.textBox_SheetCount.Text;
                    size = newOrder.comboBox_Size.Text;
                    //Integer
                    lenghtCut = Convert.ToInt32(newOrder.textBox_LengthCut.Text);
                    //Doudle
                    //thickness = newOrder.comboBox_Thickness.Text;
                    thickness = double.Parse(newOrder.comboBox_Thickness.Text.Replace('.', ','));
                    thStr = newOrder.comboBox_Thickness.Text.Replace('.', ',');
                    if (doubleSeparator == ",")
                    {
                        kF = Double.Parse(newOrder.textBox_Kf.Text.Replace('.', ','));
                    }
                    else
                    {
                        kF = Double.Parse(newOrder.textBox_Kf.Text.Replace(',', '.'));
                    }
                    //weight = double.Parse(newOrder.textBox_Weight.Text.Replace('.', ','));
                    weight = newOrder.textBox_Weight.Text;
                    if (newOrder.comboBox_Cut.Text == "Лазер" & thickness <= 20)
                    {
                        if (lenghtCut < 500)
                        {
                            string queryPrice = "SELECT * FROM PriceLL500";
                            ConnectionStringSettings conString;
                            conString = ConfigurationManager.ConnectionStrings["pathdb"];
                            string price;
                            OleDbConnection dbConn = new OleDbConnection(conString.ConnectionString);
                            OleDbCommand dbCommand = new OleDbCommand(queryPrice, dbConn);
                            dbConn.Open();
                            OleDbDataReader dbReader = dbCommand.ExecuteReader();
                            dbReader.Read();
                            price = Convert.ToString(dbReader[thStr]);
                            priceResult = Math.Round(Convert.ToDouble(price) * lenghtCut * kF);
                            string kFDetPunDB = $"{kF}|{newOrder.LB_Details}|{newOrder.LB_Punchs}";


                            string query = "UPDATE Orders set Менеджер='" + manager + "', Дата= '" + data + "',Спецификация='" +
                                           numberOfSpec + "',Заказчик='" +
                                           client + "', Вид_резки='" + cut + "', Марка_стали= '" + steelGrade +
                                           "',Толщина='" + thickness + "', Размер_листа='" +
                                           size + "', Ход='" + lenghtCut + "', Вес='" + weight + "', Кол_во_листов='" +
                                           sheetCount + "', Стоимость='" + priceResult + "', Коэффициент='" + kFDetPunDB + "' WHERE Код = " + key;

                            ConnectAccess(query);
                            flagCut = false;
                            dbConn.Close();
                        }
                        else if (lenghtCut >= 500)
                        {
                            string queryPrice = "SELECT * FROM PriceLM500";

                            ConnectionStringSettings conString;
                            conString = ConfigurationManager.ConnectionStrings["pathdb"];

                            string price;

                            OleDbConnection dbConn = new OleDbConnection(conString.ConnectionString);
                            OleDbCommand dbCommand = new OleDbCommand(queryPrice, dbConn);
                            dbConn.Open();
                            OleDbDataReader dbReader = dbCommand.ExecuteReader();
                            dbReader.Read();
                            price = Convert.ToString(dbReader[thStr]);
                            priceResult = Math.Round(Convert.ToDouble(price) * lenghtCut * kF);
                            string kFDetPunDB = $"{kF}|{newOrder.LB_Details}|{newOrder.LB_Punchs}";

                            string query = "UPDATE Orders set Менеджер='" + manager + "', Дата= '" + data + "',Спецификация='" +
                                           numberOfSpec + "',Заказчик='" +
                                           client + "', Вид_резки='" + cut + "', Марка_стали= '" + steelGrade +
                                           "',Толщина='" + thickness + "', Размер_листа='" +
                                           size + "', Ход='" + lenghtCut + "', Вес='" + weight + "', Кол_во_листов='" +
                                           sheetCount + "', Стоимость='" + priceResult + "', Коэффициент='" + kFDetPunDB + "' WHERE Код = " + key;

                            ConnectAccess(query);
                            flagCut = false;
                            dbConn.Close();
                        }
                    }


                    if (newOrder.comboBox_Cut.Text == "Плазма" & thickness >= 2 & thickness <= 32)
                    {
                        string queryPrice = "SELECT * FROM PriceP";
                        ConnectionStringSettings conString;
                        conString = ConfigurationManager.ConnectionStrings["pathdb"];

                        string price;

                        OleDbConnection dbConn = new OleDbConnection(conString.ConnectionString);
                        OleDbCommand dbCommand = new OleDbCommand(queryPrice, dbConn);
                        dbConn.Open();
                        OleDbDataReader dbReader = dbCommand.ExecuteReader();
                        dbReader.Read();
                        price = Convert.ToString(dbReader[thStr]);
                        priceResult = Math.Round(Convert.ToDouble(price) * lenghtCut * kF);
                        string kFDetPunDB = $"{kF}|{newOrder.LB_Details}|{newOrder.LB_Punchs}";

                        string query = "UPDATE Orders set Менеджер='" + manager + "', Дата= '" + data + "',Спецификация='" + numberOfSpec +
                                       "',Заказчик='" +
                                       client + "', Вид_резки='" + cut + "', Марка_стали= '" + steelGrade +
                                       "',Толщина='" + thickness + "', Размер_листа='" +
                                       size + "', Ход='" + lenghtCut + "', Вес='" + weight + "', Кол_во_листов='" +
                                       sheetCount + "', Стоимость='" + priceResult + "', Коэффициент='" + kFDetPunDB + "' WHERE Код = " + key;

                        ConnectAccess(query);
                        flagCut = false;
                        dbConn.Close();
                    }

                    if (newOrder.comboBox_Cut.Text == "Газо-кислородная" & thickness >= 16 & thickness <= 90)
                    {
                        string queryPrice = "SELECT * FROM PriceG";
                        ConnectionStringSettings conString;
                        conString = ConfigurationManager.ConnectionStrings["pathdb"];

                        string price;

                        OleDbConnection dbConn = new OleDbConnection(conString.ConnectionString);
                        OleDbCommand dbCommand = new OleDbCommand(queryPrice, dbConn);
                        dbConn.Open();
                        OleDbDataReader dbReader = dbCommand.ExecuteReader();
                        dbReader.Read();
                        price = Convert.ToString(dbReader[thStr]);
                        priceResult = Math.Round(Convert.ToDouble(price) * lenghtCut * kF);
                        string kFDetPunDB = $"{kF}|{newOrder.LB_Details}|{newOrder.LB_Punchs}";

                        string query = "UPDATE Orders set Менеджер='" + manager + "', Дата= '" + data + "',Спецификация='" + numberOfSpec +
                                       "',Заказчик='" +
                                       client + "', Вид_резки='" + cut + "', Марка_стали= '" + steelGrade +
                                       "',Толщина='" + thickness + "', Размер_листа='" +
                                       size + "', Ход='" + lenghtCut + "', Вес='" + weight + "', Кол_во_листов='" +
                                       sheetCount + "', Стоимость='" + priceResult + "', Коэффициент='" + kFDetPunDB + "' WHERE Код = " + key;

                        ConnectAccess(query);
                        flagCut = false;
                        dbConn.Close();
                    }

                    if (flagCut)
                    {
                        MessageBox.Show("Не подходящие параметры для выбранного инструмента", "Внимание...",
                            MessageBoxButtons.OK, MessageBoxIcon.Information);
                        Edit();
                    }

                    UpdateDataGrid();
                }
            }
        }
        private string SpacesCount(string str, byte count)
        {
            for (byte i = Convert.ToByte(str.Length); i < count; i++)
            {
                str = str.Insert(0, "  ");
            }
            return str;
        }

        List<int> numberSelectedRows = new List<int>();
        List<int> numberUsedRows = new List<int>();


        private void PrintResult()
        {
            byte cutLen = 0;
            byte steelGradeLen = 0;
            byte thicknessLen = 0;
            byte sheetCountLen = 0;
            byte weightLen = 0;
            byte priceRLen = 0;

            for (int i = Grid_Order.SelectedRows.Count - 1; i >= 0; i--)
            {
                if (!numberSelectedRows.Contains(Grid_Order.SelectedRows[i].Index))
                {
                    numberSelectedRows.Add(Grid_Order.SelectedRows[i].Index);
                }
            }

            if (Grid_Order.Rows.Count != 0)
            {
                foreach (var i in numberSelectedRows)
                {
                    if (!numberUsedRows.Contains(i))
                    {
                        string stellGrade, thickness, size, sheetCount, weight, priceR, cut;

                        cut = Convert.ToString(Grid_Order.Rows[Grid_Order.Rows[i].Index].Cells[5].Value);
                        stellGrade = Convert.ToString(Grid_Order.Rows[Grid_Order.Rows[i].Index].Cells[6].Value);
                        thickness = Convert.ToString(Grid_Order.Rows[Grid_Order.Rows[i].Index].Cells[7].Value);
                        size = Convert.ToString(Grid_Order.Rows[Grid_Order.Rows[i].Index].Cells[8].Value);
                        sheetCount = Convert.ToString(Grid_Order.Rows[Grid_Order.Rows[i].Index].Cells[11].Value);
                        weight = (Convert.ToString(Grid_Order.Rows[Grid_Order.Rows[i].Index].Cells[10].Value).Replace('.', ','));
                        priceR = Convert.ToString(Grid_Order.Rows[Grid_Order.Rows[i].Index].Cells[12].Value);
                        if (cutLen < cut.Length) cutLen = Convert.ToByte(cut.Length);
                        if (steelGradeLen < stellGrade.Length) steelGradeLen = Convert.ToByte(stellGrade.Length);
                        if (thicknessLen < thickness.Length) thicknessLen = Convert.ToByte(thickness.Length);
                        if (sheetCountLen < sheetCount.Length) sheetCountLen = Convert.ToByte(sheetCount.Length);
                        if (weightLen < weight.Length) weightLen = Convert.ToByte(weight.Length);
                        if (priceRLen < priceR.Length) priceRLen = Convert.ToByte(priceR.Length);
                    }
                }
                foreach (var i in numberSelectedRows)
                {
                    if (!numberUsedRows.Contains(i))
                    {
                        string stellGrade, thickness, size, sheetCount, weight, priceR, cut;
                        int priceRnumder;

                        cut = Convert.ToString(Grid_Order.Rows[Grid_Order.Rows[i].Index].Cells[5].Value);
                        stellGrade = Convert.ToString(Grid_Order.Rows[Grid_Order.Rows[i].Index].Cells[6].Value);
                        thickness = Convert.ToString(Grid_Order.Rows[Grid_Order.Rows[i].Index].Cells[7].Value);
                        size = Convert.ToString(Grid_Order.Rows[Grid_Order.Rows[i].Index].Cells[8].Value);
                        sheetCount = Convert.ToString(Grid_Order.Rows[Grid_Order.Rows[i].Index].Cells[11].Value);
                        weight = (Convert.ToString(Grid_Order.Rows[Grid_Order.Rows[i].Index].Cells[10].Value).Replace('.', ','));
                        priceRnumder= Convert.ToInt32(Grid_Order.Rows[Grid_Order.Rows[i].Index].Cells[12].Value);
                        if(priceRnumder < 500) { priceRnumder = 500; }
                        priceR = Convert.ToString(priceRnumder);
                       
                        cut = SpacesCount(cut, cutLen);
                        stellGrade = SpacesCount(stellGrade, steelGradeLen);
                        thickness = SpacesCount(thickness, thicknessLen);
                        sheetCount = SpacesCount(sheetCount, sheetCountLen);
                        weight = SpacesCount(weight, weightLen);
                        priceR = SpacesCount(priceR, priceRLen);
                        

                        richTextBox_Result.Text = richTextBox_Result.Text + "Лист " + thickness + "x" + size + "," + "\t" + "   Сталь " + stellGrade + "," + "\t"
                            + "   " + sheetCount + " шт." + "\t" + "   Вес " + weight + " кг." + "\t" + "   Резка " + priceR + " ₽." + "\t" + "   резка " + cut + "\n";
                        numberUsedRows.Add(i);
                    }
                }
            }
        }

        private void ClearTextResult()
        {
            richTextBox_Result.Text = "";
            numberSelectedRows.Clear();
            numberUsedRows.Clear();
        }
        
        private void CopyComment()
        {
            if (Grid_Order.Rows.Count != 0)
            {
                
                int index = Grid_Order.CurrentCell.RowIndex;
                string numBLCB = (Convert.ToString(Grid_Order.Rows[index].Cells[13].Value));

                if (Grid_Order.SelectedRows.Count == 1)
                {
                    if (numBLCB.Length > 5)
                    {

                        string numSpec, manager, client, thickness;
                        numSpec = Convert.ToString(Grid_Order.Rows[index].Cells[3].Value);
                        manager = Convert.ToString(Grid_Order.Rows[index].Cells[2].Value);
                        client = Convert.ToString(Grid_Order.Rows[index].Cells[4].Value);
                        thickness = Convert.ToString(Grid_Order.Rows[index].Cells[7].Value);
                        numBLCB = Convert.ToString(Grid_Order.Rows[index].Cells[13].Value);
                        Clipboard.SetText($"({thickness} мм)  {numSpec},  {manager},  ({client}),  {numBLCB}");                        
                        statusStrip.Visible = true;
                        toolStripStatusLabel_Info.Text = "Комментарий скопирован";
                    }
                    else
                    {
                        MessageBox.Show("Номер реализации не добавлен", "Внимание...",
                       MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }

                }
                else
                {
                    MessageBox.Show("Нужно выбрать одну строку", "Внимание...",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                }

            }
        }
        private void CopyCommentTelegramm()
        {
            string commResultLaser = "";
            string commResultPlasma = "";
            string commResultGas = "";
            if (Grid_Order.Rows.Count != 0)
            {
                string numBLCB = (Convert.ToString(Grid_Order.CurrentRow.Cells[13].Value));
                if (numBLCB.Length > 5)
                {
                    numBLCB = numBLCB.Substring(numBLCB.Length - 5);
                }

                for (int i = Grid_Order.SelectedRows.Count - 1; i >= 0; i--)
                {
                    numberSelectedRows.Add(Grid_Order.SelectedRows[i].Index);
                }

                if (Grid_Order.Rows.Count != 0)
                {
                    foreach (var i in numberSelectedRows)
                    {
                        string manager, client, thikchess, steelGreed, typeCut;
                        manager = Convert.ToString(Grid_Order.Rows[Grid_Order.Rows[i].Index].Cells[2].Value);
                        client = Convert.ToString(Grid_Order.Rows[Grid_Order.Rows[i].Index].Cells[4].Value);
                        thikchess = Convert.ToString(Grid_Order.Rows[Grid_Order.Rows[i].Index].Cells[7].Value);
                        steelGreed = Convert.ToString(Grid_Order.Rows[Grid_Order.Rows[i].Index].Cells[6].Value);
                        typeCut = Convert.ToString(Grid_Order.Rows[Grid_Order.Rows[i].Index].Cells[5].Value);

                        string commResultHeader;
                        if (numBLCB.Length == 5)
                        {
                            {
                                commResultHeader = $"{numBLCB.TrimStart('0')}  {manager} ({client})\n";

                                if (typeCut == "Лазер")
                                {
                                    commResultLaser += $"**{thikchess} мм {steelGreed}**  \n";
                                }
                                if (typeCut == "Плазма")
                                {
                                    commResultPlasma += $"**{thikchess} мм {steelGreed}**  \n";
                                }
                                if (typeCut == "Газо-кислородная")
                                {
                                    commResultGas += $"**{thikchess} мм {steelGreed}**  \n";
                                }
                            }
                        }
                        else
                        {
                            MessageBox.Show("Номер реализации не добавлен", "Внимание...",
                             MessageBoxButtons.OK, MessageBoxIcon.Information);
                            return;
                        }
                        string commResult = commResultHeader;
                        if (commResultLaser != "")
                        {
                            commResult += $"{commResultLaser.TrimEnd('\n')} на Лазер\n\n";
                        }
                        if (commResultPlasma != "")
                        {
                            commResult += $"{commResultPlasma.TrimEnd('\n')} на Плазму\n\n";
                        }
                        if (commResultGas != "")
                        {
                            commResult += $"{commResultGas.TrimEnd('\n')} на Кристалл резка газом";
                        }
                        if (commResult != "")
                        {
                            Clipboard.SetText(commResult);
                            
                            AddTextInfo(statusStrip, toolStripStatusLabel_Info, "Комментарий для Telegram скопирован");
                            
                        }
                    }
                    numberSelectedRows.Clear();
                }
            }
        }        

        void MainFormLoad(object sender, EventArgs e)
        {
           
            
            Grid_Order.GetType().GetProperty("DoubleBuffered", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(Grid_Order, true, null);
            if (System.Windows.Forms.Application.OpenForms["MainForm"] != null)
            {
                loadForm.Close();
            }
        }

        void Button_UpdateClick(object sender, EventArgs e)
        {
            checkBox_SortChoiceDayON.Checked = false;
            UpdateDataGrid();
        }

        void MainFormFormClosing(object sender, FormClosingEventArgs e)
        {
            const string message =
                "Вы хотите закрыть Журнал заявок?";
            const string caption = "Выход...";
            var result = MessageBox.Show(message, caption,
                             MessageBoxButtons.YesNo,
                             MessageBoxIcon.Question);
            if (result == DialogResult.No)
            {
                e.Cancel = true;
            }
        }

        void Button_NewOrderClick(object sender, EventArgs e)
        {
            NewOrd();
        }

        void Button_DeleteClick(object sender, EventArgs e)
        {
            //Кнопка удалить___________________________________________________
            Delete();
        }

        void Button_EditClick(object sender, EventArgs e)
        {
            //Кнопка редактирования_________________________________
            Edit();
        }

        void РедактироватьToolStripMenuItemClick(object sender, EventArgs e)
        {
            Edit();
        }

        void УдалитьToolStripMenuItemClick(object sender, EventArgs e)
        {
            Delete();
        }


        void Button_ResultClick(object sender, EventArgs e)
        {
            PrintResult();
        }

        void Button_CopyResultClick(object sender, EventArgs e)
        {
            if (richTextBox_Result.Text.Length != 0)
            {
                DataObject dto = new DataObject();
                dto.SetText(richTextBox_Result.Rtf, TextDataFormat.Rtf);
                dto.SetText(richTextBox_Result.Text, TextDataFormat.UnicodeText);
                Clipboard.Clear();
                Clipboard.SetDataObject(dto);
                statusStrip.Visible = true;
                toolStripStatusLabel_Info.Text = "Результат просчёта скопирован";
            }
        }

        void РезультатToolStripMenuItemClick(object sender, EventArgs e)
        {
            PrintResult();
        }

        void Button1Click(object sender, EventArgs e)
        {
            ClearTextResult();
        }

        void CheckBox_DateDayCheckedChanged(object sender, EventArgs e)
        {
            LoadDataSortDay();
        }

        void ДобавитьБЛЦБToolStripMenuItemClick(object sender, EventArgs e)
        {
            AddBLCB_In_DB();
        }

        void Button_ColorInWorkClick(object sender, EventArgs e)
        {
            if (Grid_Order.Rows.Count != 0)
            {
                Grid_Order.CurrentRow.DefaultCellStyle.ForeColor = Color.White;
                int key = Convert.ToInt32(Grid_Order.CurrentRow.Index);
                ColorInDB(-882036);
                ColorRow(-882036, key);
            }

            UpdateDataGrid();
        }

        void Button_ColorFinishClick(object sender, EventArgs e)
        {
            if (Grid_Order.Rows.Count != 0)
            {
                Grid_Order.CurrentRow.DefaultCellStyle.ForeColor = Color.White;
                int key = Convert.ToInt32(Grid_Order.CurrentRow.Index);
                ColorInDB(-10238087);
                ColorRow(-10238087, key);
                UpdateDataGrid();
            }
        }

        void Button_SentClick(object sender, EventArgs e)
        {
            if (Grid_Order.Rows.Count != 0)
            {
                Grid_Order.CurrentRow.DefaultCellStyle.ForeColor = Color.White;
                int key = Convert.ToInt32(Grid_Order.CurrentRow.Index);
                ColorInDB(-8154114);
                ColorRow(-8154114, key);
                UpdateDataGrid();
            }
        }

        void Button_ColorDialogClick(object sender, EventArgs e)
        {                     
            if (Grid_Order.Rows.Count != 0 && !checkBox_checkColor.Checked)
            {
                if (colorDialog.ShowDialog() == DialogResult.OK)
                {
                    int color = colorDialog.Color.ToArgb();                   
                    int key = Convert.ToInt32(Grid_Order.CurrentRow.Index);
                    ColorInDB(color);
                    button_ColorDialog.BackColor = System.Drawing.Color.FromArgb(color);
                    ColorRow(color, key);
                    UpdateDataGrid();
                }
            }
            if (checkBox_checkColor.Checked)
            {
                if (colorDialog.ShowDialog() == DialogResult.OK)
                {
                    int color = colorDialog.Color.ToArgb();
                    richTextBox_Result.Text = color.ToString();
                }
                checkBox_checkColor.Checked = false;
            }
        }

        void Timer_ColorTick(object sender, EventArgs e)
        {
            RefrechColor();
        }

        void Button_ClearColorClick(object sender, EventArgs e)
        {
            if (Grid_Order.Rows.Count != 0)
            {
                ColorInDB(-1);
                UpdateDataGrid();
            }
        }

        void CheckBox_InWorkCheckedChanged(object sender, EventArgs e)
        {
            LoadInWork();
        }

        void CheckBox_FinishedCheckedChanged(object sender, EventArgs e)
        {
            LoadFinished();
        }

        void CheckBox_SentCheckedChanged(object sender, EventArgs e)
        {
            LoadSented();
        }

        void ВРаботеToolStripMenuItemClick(object sender, EventArgs e)
        {
            if (Grid_Order.Rows.Count != 0)
            {
                Grid_Order.CurrentRow.DefaultCellStyle.ForeColor = Color.White;
                int key = Convert.ToInt32(Grid_Order.CurrentRow.Index);
                ColorInDB(-882036);
                ColorRow(-882036, key);
                UpdateDataGrid();
            }
        }

        void ГотовоToolStripMenuItemClick(object sender, EventArgs e)
        {
            if (Grid_Order.Rows.Count != 0)
            {
                Grid_Order.CurrentRow.DefaultCellStyle.ForeColor = Color.White;
                int key = Convert.ToInt32(Grid_Order.CurrentRow.Index);
                ColorInDB(-10238087);
                ColorRow(-10238087, key);
                UpdateDataGrid();
            }
        }

        void ToolStripMenuItem1Click(object sender, EventArgs e)
        {
            if (Grid_Order.Rows.Count != 0)
            {
                Grid_Order.CurrentRow.DefaultCellStyle.ForeColor = Color.White;
                int key = Convert.ToInt32(Grid_Order.CurrentRow.Index);
                ColorInDB(-8154114);
                ColorRow(-8154114, key);
                UpdateDataGrid();
            }
        }

        void ДругойЦветToolStripMenuItemClick(object sender, EventArgs e)
        {
            if (Grid_Order.Rows.Count != 0)
            {
                if (colorDialog.ShowDialog() == DialogResult.OK)
                {
                    int color = colorDialog.Color.ToArgb();
                    int key = Convert.ToInt32(Grid_Order.CurrentRow.Index);
                    ColorInDB(color);
                    button_ColorDialog.BackColor = Color.FromArgb(color);
                    ColorRow(color, key);
                    UpdateDataGrid();
                }
            }
        }

        void СнятьВыделениеToolStripMenuItemClick(object sender, EventArgs e)
        {
            if (Grid_Order.Rows.Count != 0)
            {
                ColorInDB(-1);
                UpdateDataGrid();
            }
        }

        void TextBox_SearhKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                //Searh();
            }

            if (e.KeyCode == Keys.Escape)
            {
                textBox_Searh.Text = "";
            }
        }

        void ДобавитьБЛЦБToolStripMenuItem1Click(object sender, EventArgs e)
        {
            if (Grid_Order.Rows.Count != 0)
            {
                AddBLCB_In_DB();
            }
        }


        void Button_SortChoiceDayClick(object sender, EventArgs e)
        {
            ClearTextResult();
            checkBox_SortChoiceDayON.Checked = true;
            SortChoiceDate();
        }
        void КопироватьКомментарийToolStripMenuItem_Click(object sender, EventArgs e)
        {
            CopyComment();
        }
       
        void TextBox_Searh_TextChanged(object sender, EventArgs e)
        {
            if (comboBox1.Text == "Толщина")
            {
                (Grid_Order.DataSource as System.Data.DataTable).DefaultView.RowFilter = string.Format("Convert(" + comboBox1.Text + ",'System.String') LIKE '{0}'", textBox_Searh.Text);
            }
            else
            {
                (Grid_Order.DataSource as System.Data.DataTable).DefaultView.RowFilter = string.Format("[{0}] LIKE '%{1}%'", comboBox1.Text, textBox_Searh.Text);
            }
        }
        void ComboBox1_SelectedIndexChanged(object sender, EventArgs e)
        {
            ClearText(textBox_Searh);
        }
        void TextBox_Searh_KeyPress(object sender, KeyPressEventArgs e)
        {
            if (e.KeyChar == '\'')
            {
                e.Handled = true;
            }
        }

        private void TelegramКомментарийToolStripMenuItem_Click(object sender, EventArgs e)
        {
            CopyCommentTelegramm();
        }

        private void Button_Clear_Searh_Click(object sender, EventArgs e)
        {
            ClearText(textBox_Searh);
        }

        private void TelegramКомментарийToolStripMenuItem1_Click(object sender, EventArgs e)
        {
            CopyCommentTelegramm();
        }

        private void Grid_Order_SelectionChanged(object sender, EventArgs e)
        {
            ColorSelectedRows(Grid_Order);
        }

        private void MainForm_Activated(object sender, EventArgs e)
        {
            ClearInfo(toolStripStatusLabel_Info);
        }

        private void button_Update_MouseEnter(object sender, EventArgs e)
        {
            statusStrip.Visible = true;
            AddTextInfo(statusStrip, toolStripStatusLabel_Info, "Синхронизировать с базой данных");
        }

    }
}