using System;
using System.Configuration;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using static Jornal.Clasess.CalculationCoefficient;
using static Jornal.ExtractText;
using static Jornal.TextBoxMetods;

namespace Jornal
{

    public partial class Form_NewOrder : Form
    {
        public Form_NewOrder()
        {
            InitializeComponent();
            string[] Managers = ConfigurationManager.AppSettings["Managers"]?.Split(',') ?? new string[0];
            string[] Customers = ConfigurationManager.AppSettings["Customers"]?.Split(',') ?? new string[0];
            AutoCompleteStringCollection managersCollection = new AutoCompleteStringCollection();
            AutoCompleteStringCollection customersCollection = new AutoCompleteStringCollection();
            managersCollection.AddRange(Managers);
            customersCollection.AddRange(Customers);

            textBox_Manager.AutoCompleteCustomSource = managersCollection;
            textBox_Manager.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
            textBox_Manager.AutoCompleteSource = AutoCompleteSource.CustomSource;

            textBox_Client.AutoCompleteCustomSource = customersCollection;
            textBox_Client.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
            textBox_Client.AutoCompleteSource = AutoCompleteSource.CustomSource;

        }
        public string SpecaFilePath { get; set; }
        public string tBDate
        {
            get
            {
                return textBox_date.Text;
            }
            set
            {
                textBox_date.Text = value;
            }
        }
        public string tBManager
        {
            get
            {
                return this.textBox_Manager.Text;
            }
            set
            {
                this.textBox_Manager.Text = value;
            }
        }
        public string tBNSpec
        {
            get
            {
                return this.textBox_NumSpec.Text;
            }
            set
            {
                this.textBox_NumSpec.Text = value;
            }
        }
        public string tBClient
        {
            get
            {
                return this.textBox_Client.Text;
            }
            set
            {
                this.textBox_Client.Text = value;
            }
        }
        public string cBCut
        {
            get
            {
                return this.comboBox_Cut.Text;
            }
            set
            {
                this.comboBox_Cut.Text = value;
            }
        }
        public string cBSteelGrade
        {
            get
            {
                return this.comboBox_SteelGreed.Text;
            }
            set
            {
                this.comboBox_SteelGreed.Text = value;
            }
        }
        public string cBThickness
        {
            get
            {
                return this.comboBox_Thickness.Text;
            }
            set
            {
                this.comboBox_Thickness.Text = value;
            }
        }
        public string cBSize
        {
            get
            {
                return this.comboBox_Size.Text;
            }
            set
            {
                this.comboBox_Size.Text = value;

            }
        }
        public string tBLenCut
        {
            get
            {
                return this.textBox_LengthCut.Text;
            }
            set
            {
                this.textBox_LengthCut.Text = value;

            }
        }
        public string tBWeight
        {
            get
            {
                return this.textBox_Weight.Text;
            }
            set
            {
                this.textBox_Weight.Text = value;

            }
        }
        public string tBSheetCount
        {
            get
            {
                return this.textBox_SheetCount.Text;
            }
            set
            {
                this.textBox_SheetCount.Text = value;

            }
        }
        public string tBKf
        {
            get
            {
                return this.textBox_Kf.Text;
            }
            set
            {
                this.textBox_Kf.Text = value;
            }
        }
        public string cntDetPun;
        public string LB_Details
        {
            get
            {
                return label_Details.Text;
            }
            set { label_Details.Text = value; }
        }
        public string LB_Punchs
        {
            get
            {
                return label_Punchs.Text;
            }
            set
            {
                label_Punchs.Text = value;
            }
        }
        public string idMat
        {
            get
            {
                return comboBox_ID_MAT.Text;
            }
            set
            {
                comboBox_ID_MAT.Text = value;
            }
        }
        public string lbMat
        {
            get
            {
                return label_ID_MAT.Text;
            }
            set
            {
                label_ID_MAT.Text = value;
            }
        }
        public string lbCurGas
        {
            get
            {
                return label_CUR_GAS.Text;
            }
            set
            {
                label_CUR_GAS.Text = value;
            }
        }

        private async void Form1Load(object sender, EventArgs e)
        {
            DateTime today = DateTime.Now;
            //  textBox_MyDate.Text = today.ToShortDateString();
            AcceptButton = button_Ok;
            CancelButton = button_Cancel;
            if (SpecaFilePath != null)
            {
                textBox_Run.Text = await ProcessSpecaFileAsync(SpecaFilePath);
                button_Run.PerformClick();
            }

        }
        private async Task<string> ProcessSpecaFileAsync(string specaFilePath)
        {
            string extractedTextSpeca = string.Empty;
            if (!string.IsNullOrEmpty(specaFilePath))
            {
                string extension = System.IO.Path.GetExtension(specaFilePath).ToLower();

                if (extension == ".pdf")
                {
                    extractedTextSpeca = "*";
                    extractedTextSpeca += await Task.Run(() => ExtractTextFromPdf(specaFilePath));
                }
                else if (extension == ".html")
                {
                    extractedTextSpeca = " ";
                    extractedTextSpeca += await Task.Run(() => ExtractTextFromHtml(specaFilePath));
                }
                textBox_Run.Text = extractedTextSpeca;
            }
            return extractedTextSpeca;
        }
        void Button_OkClick(object sender, EventArgs e)
        {

            if (textBox_Manager.Text.Length == 0 || textBox_NumSpec.Text.Length == 0 || textBox_Client.Text.Length == 0 || comboBox_Cut.Text.Length == 0 ||
                comboBox_SteelGreed.Text.Length == 0 || comboBox_Thickness.Text.Length == 0 || comboBox_Size.Text.Length == 0 || textBox_LengthCut.Text.Length == 0 ||
                textBox_Weight.Text.Length == 0 || textBox_SheetCount.Text.Length == 0)
            {
                MessageBox.Show("Поля не должны быть пустыми", "Что-то пошло не так", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else
            {
                DialogResult = DialogResult.OK;
                Close();
            }
        }
        void Button_RunClick(object sender, EventArgs e)
        {
            if (textBox_Run.Text.Length == 0)
            {
                MessageBox.Show("Поле не должно быть пустым", "Что-то пошло не так", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else
            {
                bool flag = false;
                //***************Speca HansLaser******************
                if ((textBox_Run.Text[0] == '*' && textBox_Run.Text[1] == '@') || textBox_Run.Text[0] == '@')
                {
                    bool isDropText = textBox_Run.Text[0] == '*';
                    groupBox_ID_MAT.Visible = true;
                    flag = true;//check spec
                    string[] hansLaserSpecArray = textBox_Run.Text.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                    //for (int i = 0; i < hansLaserSpecArray.Length; i++)
                    //{
                    //    MessageBox.Show($"{i}--{hansLaserSpecArray[i]}");
                    //}
                    string[] dateSpec = Regex.Split(hansLaserSpecArray[10], "-");//Date
                    string[] infoSheets = Regex.Split(hansLaserSpecArray[(isDropText ? 30 : 25)], " ");//infomation of sheets
                    string valueStr = hansLaserSpecArray[17].Trim();
                    double lenghtCut = Math.Ceiling(Convert.ToDouble(valueStr, CultureInfo.InvariantCulture) / 1000);
                    tBDate = $"{dateSpec[2]}.{dateSpec[1]}.{dateSpec[0]}";//data
                    tBNSpec = $"{dateSpec[1]}{dateSpec[2]}-{(dateSpec[3] == "" ? dateSpec[4] : dateSpec[3])}";//№Speca                    
                    cBCut = "HansLaser";
                    cBSteelGrade = $"{infoSheets[(isDropText ? 0 : 3)].Split('_')[0]}";
                    idMat = $"{infoSheets[(isDropText ? 0 : 3)].Split('_')[1]}";
                    label_CUR_GAS.Text = $"Газ:  {(isDropText ? hansLaserSpecArray[31].Split(' ')[1] : infoSheets[4])}";
                    cBThickness = $"{(isDropText ? hansLaserSpecArray[29].TrimStart().Split(' ')[1] : infoSheets[5])}";
                    tBSheetCount = $"{hansLaserSpecArray[13]}";
                    cBSize = $"{infoSheets[2]}x{infoSheets[1]}";
                    tBLenCut = lenghtCut.ToString();
                    tBWeight = $"{hansLaserSpecArray[15]}";
                    string kf = "1.0";
                    string coutnDetail = $"{hansLaserSpecArray[11]}".Trim();
                    double cntDetails = Convert.ToDouble(coutnDetail.Trim());
                    string countPunch = $"{hansLaserSpecArray[16]}".Trim();
                    double cntPunch = Convert.ToDouble(countPunch.Trim());
                    double propotions = Math.Round(cntPunch / cntDetails);
                    kf = CoefficientOfPunch(propotions);
                    tBKf = kf;
                    label_Details.Text = coutnDetail;
                    label_Punchs.Text = countPunch;
                    cntDetPun = $"{coutnDetail} | {countPunch}";
                }
                else
                {
                    if ((textBox_Run.Text.Substring(0, 1)) == " ")
                    {               //условие для спецификации из винтех
                                    // Автозаполение толщины__________________________________________
                        string[] th = Regex.Split(textBox_Run.Text, "Габариты: ");
                        if (th.Length < 2)
                        {
                            flag = true;
                        }
                        else
                        {

                            string[] th1 = Regex.Split(th[1], "x");
                            if (th1.Length < 2)
                            {
                                flag = true;
                            }
                            else
                            {
                                string th2 = th1[0];
                                string[] th3 = th2.Split('.');
                                if (th3.Length < 2)
                                {
                                    flag = true;
                                }
                                else
                                {
                                    if (th3[1] == "0")
                                    {
                                        comboBox_Thickness.Text = th3[0];
                                    }
                                    else
                                    {
                                        comboBox_Thickness.Text = th3[0] + ',' + th3[1];
                                    }
                                }
                            }

                        }
                        //Автозаполнение марки стали___________________________________
                        string[] sG = Regex.Split(textBox_Run.Text, "Материал: ");
                        if (sG.Length < 2)
                        {
                            flag = true;
                        }
                        else
                        {
                            string[] sG1 = Regex.Split(sG[1], " ");
                            string[] sG2 = Regex.Split(sG1[0], " ");
                            comboBox_SteelGreed.Text = sG2[0];
                        }
                        //size sheet
                        string[] sz = Regex.Split(textBox_Run.Text, "Габариты: ");
                        if (sz.Length < 2)
                        {
                            flag = true;
                        }
                        else
                        {
                            string[] sz1 = Regex.Split(sz[1], " ");
                            string[] sz2 = Regex.Split(sz1[0], "x");
                            comboBox_Size.Text = sz2[2] + "x" + sz2[1];
                        }

                        //lenght cut
                        string[] lC = Regex.Split(textBox_Run.Text, "реза:");
                        if (lC.Length < 2)
                        {
                            flag = true;
                        }
                        else
                        {
                            string[] lC1 = Regex.Split(lC[1], " ");
                            string[] lC2 = Regex.Split(lC1[1], ".");
                            string lCSt = lC1[1];
                            string[] res = lCSt.Split('.');
                            int lCInt = Convert.ToInt32(res[0]);
                            string lCSt2 = Convert.ToString((lCInt / 1000) + 1);
                            textBox_LengthCut.Text = (lCSt2);
                        }
                        //Weight
                        string[] w = Regex.Split(textBox_Run.Text, "кг");
                        if (w.Length < 2)
                        {
                            flag = true;
                        }
                        else
                        {
                            string[] w1 = Regex.Split(w[1], " ");
                            textBox_Weight.Text = w1[1];
                        }
                        //Sheet count
                        string[] sCnt = Regex.Split(textBox_Run.Text, "Листов:");
                        if (sCnt.Length < 2)
                        {
                            flag = true;

                        }
                        else
                        {
                            string[] sCnt1 = Regex.Split(sCnt[1], " ");
                            textBox_SheetCount.Text = sCnt1[1];
                        }

                        string kF = "1.0";
                        string[] countDetail = Regex.Split(textBox_Run.Text, "Общие кол-во деталей:");
                        string[] cD = Regex.Split(countDetail[1], " ");
                        double counDet = Convert.ToDouble(cD[1].Trim());
                        string[] countPunch = Regex.Split(textBox_Run.Text, "Пробивки:");
                        string[] cP = Regex.Split(countPunch[1], " ");
                        double counPun = Convert.ToDouble(cP[1].Trim());
                        double propotions = Math.Round(counPun / counDet);
                        kF = CoefficientOfPunch(propotions);      //Коефициент от кол-во пробивок
                        textBox_Kf.Text = kF;
                        label_Details.Text = cD[1].Trim();
                        label_Punchs.Text = cP[1].Trim();
                        cntDetPun = $"{cD[1].Trim()}|{cP[1].Trim()}";
                    }
                    else
                    {               //условие для снецификации из браузера
                                    //толщина
                        string[] th = Regex.Split(textBox_Run.Text, "Габариты:");

                        if (th.Length < 2)
                        {
                            flag = true;
                        }
                        else
                        {
                            string[] th1 = Regex.Split(th[1], "\t");
                            if (th1.Length < 2)
                            {
                                flag = true;
                            }
                            else
                            {
                                string[] th2 = Regex.Split(th1[1], "x");
                                if (th2.Length < 2)
                                {
                                    flag = true;
                                }
                                else
                                {
                                    string th3 = th2[0];
                                    string[] th4 = th3.Split('.');
                                    if (th4.Length < 2)
                                    {
                                        flag = true;
                                    }
                                    else
                                    {
                                        if (th4[1] == "0")
                                        {
                                            comboBox_Thickness.Text = th4[0];
                                        }
                                        else
                                        {
                                            comboBox_Thickness.Text = th4[0] + ',' + th4[1];
                                        }
                                    }
                                }
                            }
                            string kF = "1.0";
                            string[] countDetail = Regex.Split(textBox_Run.Text, "Общие кол-во деталей:");
                            string[] cD = Regex.Split(countDetail[1], " ");
                            double counDet = Convert.ToDouble(cD[0].Trim());
                            string[] countPunch = Regex.Split(textBox_Run.Text, "Пробивки:");
                            string[] cP = Regex.Split(countPunch[1], " ");
                            double counPun = Convert.ToDouble(cP[0].Trim());
                            double propotions = Math.Round(counPun / counDet);
                            kF = CoefficientOfPunch(propotions); //Коефициент от кол-во пробивок
                            textBox_Kf.Text = kF;
                            label_Details.Text = cD[0].Trim();
                            label_Punchs.Text = cP[0].Trim();
                            cntDetPun = $"{cD[0].Trim()}|{cP[0].Trim()}";
                        }

                        //Автозаполнение марки стали___________________________________

                        string[] sG = Regex.Split(textBox_Run.Text, "Материал:");
                        if (sG.Length < 2)
                        {
                            flag = true;
                        }
                        else
                        {
                            string[] sG1 = Regex.Split(sG[1], " ");
                            string[] sG2 = Regex.Split(sG1[0], "\t");
                            if (sG2.Length < 2)
                            {
                                flag = true;
                            }
                            else
                            {
                                comboBox_SteelGreed.Text = sG2[1];
                            }
                        }
                        //Size sheet
                        string[] sz = Regex.Split(textBox_Run.Text, "Габариты:");
                        if (sz.Length < 2)
                        {
                            flag = true;
                        }
                        else
                        {
                            string[] sz1 = Regex.Split(sz[1], "\t");
                            if (sz1.Length < 2)
                            {
                                flag = true;
                            }
                            else
                            {
                                string[] sz2 = Regex.Split(sz1[1], "x");
                                comboBox_Size.Text = sz2[2] + "x" + sz2[1];
                            }
                        }

                        //lenght cut
                        string[] lC = Regex.Split(textBox_Run.Text, "реза:");
                        if (lC.Length < 2)
                        {
                            flag = true;
                        }
                        else
                        {
                            string[] lC1 = Regex.Split(lC[1], "\t");
                            if (lC1.Length < 2)
                            {
                                flag = true;
                            }
                            else
                            {
                                string lCSt = lC1[1];
                                string[] res = lCSt.Split('.');
                                int lCInt = Convert.ToInt32(res[0]);
                                string lCSt2 = Convert.ToString((lCInt / 1000) + 1);
                                textBox_LengthCut.Text = (lCSt2);
                            }
                        }
                        //Weidht
                        string[] w = Regex.Split(textBox_Run.Text, "кг");
                        if (w.Length < 2)
                        {
                            flag = true;
                        }
                        else
                        {
                            string[] w1 = Regex.Split(w[1], "\t");
                            if (w1.Length < 2)
                            {
                                flag = true;
                            }
                            else
                            {
                                textBox_Weight.Text = w1[1];
                            }
                        }
                        //Sheet count
                        string[] sCnt = Regex.Split(textBox_Run.Text, "Листов:");
                        if (sCnt.Length < 2)
                        {
                            flag = true;
                        }
                        else
                        {
                            string[] sCnt1 = Regex.Split(sCnt[1], "\t");
                            if (sCnt1.Length < 2)
                            {
                                flag = true;
                            }
                            else
                            {
                                textBox_SheetCount.Text = sCnt1[1];
                            }
                        }
                    }
                    //Автозаполнение номер спецификации__________________________________				
                    string[] nS = Regex.Split(textBox_Run.Text, "Спецификация №");
                    if (nS.Length < 2)
                    {
                        flag = true;
                    }
                    else
                    {
                        string[] nS1 = Regex.Split(nS[1], "-");
                        if (nS1[3] == "")
                        {
                            textBox_NumSpec.Text = nS1[1] + nS1[2] + "-" + nS1[4];
                        }
                        else
                        {
                            textBox_NumSpec.Text = nS1[1] + nS1[2] + "-" + nS1[3];
                        }
                    }
                    if (flag)
                    {
                        MessageBox.Show("Кажется этот текст не из спецификации БЛОК", "Что-то пошло не так", MessageBoxButtons.OK, MessageBoxIcon.Information);

                    }
                    string[] dates = Regex.Split(textBox_Run.Text, "Спецификация №");
                    if (dates.Length < 2)
                    {
                        flag = true;
                    }
                    else
                    {
                        string[] dates1 = Regex.Split(dates[1], "-");
                        string[] year = Regex.Split(dates1[0], " ");
                        textBox_date.Text = $"{dates1[2]}.{dates1[1]}.{year[3]}";
                    }

                    //автозаполнение тип резки
                    string[] typeCut = Regex.Split(textBox_Run.Text, ".rcam");

                    string typeCutCB = typeCut[0];


                    if (typeCutCB.Contains("ЛАЗЕР"))
                    {
                        comboBox_Cut.Text = "Лазер";
                    }
                    if (typeCutCB.Contains("ПЛАЗМА"))
                    {
                        comboBox_Cut.Text = "Плазма";
                    }
                    if (typeCutCB.Contains("ГАЗО-КИСЛОРОДНАЯ"))
                    {
                        comboBox_Cut.Text = "Газо-кислородная";
                    }
                }

            }

        }
        void Button_CancelClick(object sender, EventArgs e)
        {
            Close();
        }
        void Button_ClearClick(object sender, EventArgs e)
        {
            textBox_Run.Text = "";
        }

        private void textBox_Kf_KeyPress(object sender, KeyPressEventArgs e)
        {
            CheckTBChar(textBox_Kf, e);
        }

        private void textBox_Kf_TextChanged(object sender, EventArgs e)
        {
            CheckTB(textBox_Kf);
        }

        private void comboBox_ID_MAT_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (comboBox_ID_MAT.SelectedItem != null) // Проверка, что выбран элемент
            {
                switch (comboBox_ID_MAT.SelectedItem)
                {
                    case "0":
                        label_ID_MAT.Text = "Черная сталь";
                        break;
                    case "1":
                        label_ID_MAT.Text = "Нержавеющая сталь";
                        break;
                    case "2":
                        label_ID_MAT.Text = "Цветной металл";
                        break;
                    case "-1":
                        label_ID_MAT.Text = "Default";
                        break;
                    default:
                        break;
                }
            }

        }

        private void comboBox_Cut_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (comboBox_Cut.SelectedItem != null && comboBox_Cut.SelectedIndex == 3)
            {
                groupBox_ID_MAT.Visible = true;
            }
            else
            {
                groupBox_ID_MAT.Visible = false;
            }
        }
        private readonly Color NORMAL_COLOR = SystemColors.Window;
        private readonly Color ACTIVE_COLOR = Color.Lavender;
        private readonly Color ERROR_COLOR = Color.DarkSalmon;
        private void textBox_Run_DragEnter(object sender, DragEventArgs e)
        {
            textBox_Run.Text = "";
            textBox_Run.BackColor = ACTIVE_COLOR;
            textBox_Run.BorderStyle = BorderStyle.Fixed3D;

            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var files = (string[])e.Data.GetData(DataFormats.FileDrop);

                // Проверяем поддерживаемые расширения (PDF и HTML)
                bool isSupportedFile = files.Any(f =>
                {
                    string ext = System.IO.Path.GetExtension(f).ToLower();
                    return ext == ".pdf" || ext == ".html" || ext == ".htm";
                });

                if (isSupportedFile)
                {
                    e.Effect = DragDropEffects.Copy;
                    return;
                }
                else
                {
                    textBox_Run.BackColor = ERROR_COLOR;
                    textBox_Run.Text = "Поддерживаются только спецификации PDF и HTML";
                    return;
                }
            }

            e.Effect = DragDropEffects.None;
        }

        private void textBox_Run_DragLeave(object sender, EventArgs e)
        {
            textBox_Run.BackColor = NORMAL_COLOR;
            textBox_Run.BorderStyle = BorderStyle.None;
        }

        private async void textBox_Run_DragDrop(object sender, DragEventArgs e)
        {
            textBox_Run.BackColor = NORMAL_COLOR;
            textBox_Run.Refresh();

            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var files = (string[])e.Data.GetData(DataFormats.FileDrop);
                var fileSpeca = files.FirstOrDefault();
                if (fileSpeca != null)
                {
                    string extension = System.IO.Path.GetExtension(fileSpeca).ToLower();
                    string extractedTextSpeca = string.Empty;
                    try
                    {
                        if (extension == ".pdf")
                        {
                            extractedTextSpeca = "*";
                            extractedTextSpeca += await Task.Run(() => ExtractTextFromPdf(fileSpeca));
                        }
                        else if (extension == ".html")
                        {
                            extractedTextSpeca = " ";
                            extractedTextSpeca += await Task.Run(() => ExtractTextFromHtml(fileSpeca));
                        }
                        textBox_Run.Text = extractedTextSpeca;
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Ошибка {ex.Message}");
                    }
                }
            }

        }

    }
}

