
using System;
using System.Windows.Forms;
using System.Text.RegularExpressions;
using static Jornal.TextBoxMetods;
using static Jornal.Clasess.CalculationCoefficient;
using System.Configuration;

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


        void Form1Load(object sender, EventArgs e)
        {
            DateTime today = DateTime.Now;
          //  textBox_MyDate.Text = today.ToShortDateString();
            AcceptButton = button_Ok;
            CancelButton = button_Cancel;
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
    }
}
