/*
 * Создано в SharpDevelop.
 * Пользователь: denis
 * Дата: 24.03.2022
 * Время: 0:30
 * 
 * Для изменения этого шаблона используйте меню "Инструменты | Параметры | Кодирование | Стандартные заголовки".
 */
namespace Jornal
{
	partial class Form_NewOrder
	{
		/// <summary>
		/// Designer variable used to keep track of non-visual components.
		/// </summary>
		private System.ComponentModel.IContainer components = null;

		/// <summary>
		/// Disposes resources used by the form.
		/// </summary>
		/// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
		protected override void Dispose(bool disposing)
		{
			if (disposing) {
				if (components != null) {
					components.Dispose();
				}
			}
			base.Dispose(disposing);
		}

		/// <summary>
		/// Required method for Designer support - do not modify
		/// the contents of this method with the code editor.
		/// </summary>
		private void InitializeComponent()
		{
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(Form_NewOrder));
            this.button_Ok = new System.Windows.Forms.Button();
            this.button_Cancel = new System.Windows.Forms.Button();
            this.textBox_Run = new System.Windows.Forms.TextBox();
            this.textBox_Manager = new System.Windows.Forms.TextBox();
            this.textBox_NumSpec = new System.Windows.Forms.TextBox();
            this.textBox_Client = new System.Windows.Forms.TextBox();
            this.comboBox_Cut = new System.Windows.Forms.ComboBox();
            this.comboBox_SteelGreed = new System.Windows.Forms.ComboBox();
            this.comboBox_Thickness = new System.Windows.Forms.ComboBox();
            this.comboBox_Size = new System.Windows.Forms.ComboBox();
            this.textBox_LengthCut = new System.Windows.Forms.TextBox();
            this.textBox_Weight = new System.Windows.Forms.TextBox();
            this.textBox_SheetCount = new System.Windows.Forms.TextBox();
            this.button_Run = new System.Windows.Forms.Button();
            this.label1 = new System.Windows.Forms.Label();
            this.label2 = new System.Windows.Forms.Label();
            this.label3 = new System.Windows.Forms.Label();
            this.label4 = new System.Windows.Forms.Label();
            this.label5 = new System.Windows.Forms.Label();
            this.label6 = new System.Windows.Forms.Label();
            this.label7 = new System.Windows.Forms.Label();
            this.label8 = new System.Windows.Forms.Label();
            this.label9 = new System.Windows.Forms.Label();
            this.label10 = new System.Windows.Forms.Label();
            this.panel1 = new System.Windows.Forms.Panel();
            this.button_Clear = new System.Windows.Forms.Button();
            this.label11 = new System.Windows.Forms.Label();
            this.label12 = new System.Windows.Forms.Label();
            this.textBox_Kf = new System.Windows.Forms.TextBox();
            this.label_Details = new System.Windows.Forms.Label();
            this.label_Punchs = new System.Windows.Forms.Label();
            this.label13 = new System.Windows.Forms.Label();
            this.label14 = new System.Windows.Forms.Label();
            this.textBox_date = new System.Windows.Forms.TextBox();
            this.comboBox_ID_MAT = new System.Windows.Forms.ComboBox();
            this.groupBox_ID_MAT = new System.Windows.Forms.GroupBox();
            this.label_CUR_GAS = new System.Windows.Forms.Label();
            this.label_ID_MAT = new System.Windows.Forms.Label();
            this.label15 = new System.Windows.Forms.Label();
            this.panel1.SuspendLayout();
            this.groupBox_ID_MAT.SuspendLayout();
            this.SuspendLayout();
            // 
            // button_Ok
            // 
            this.button_Ok.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.button_Ok.BackColor = System.Drawing.Color.DarkSeaGreen;
            this.button_Ok.Location = new System.Drawing.Point(156, 686);
            this.button_Ok.Name = "button_Ok";
            this.button_Ok.Size = new System.Drawing.Size(107, 38);
            this.button_Ok.TabIndex = 130;
            this.button_Ok.Text = "OK";
            this.button_Ok.UseVisualStyleBackColor = false;
            this.button_Ok.Click += new System.EventHandler(this.Button_OkClick);
            // 
            // button_Cancel
            // 
            this.button_Cancel.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.button_Cancel.BackColor = System.Drawing.Color.RosyBrown;
            this.button_Cancel.Location = new System.Drawing.Point(311, 686);
            this.button_Cancel.Name = "button_Cancel";
            this.button_Cancel.Size = new System.Drawing.Size(107, 38);
            this.button_Cancel.TabIndex = 140;
            this.button_Cancel.Text = "Отмена";
            this.button_Cancel.UseVisualStyleBackColor = false;
            this.button_Cancel.Click += new System.EventHandler(this.Button_CancelClick);
            // 
            // textBox_Run
            // 
            this.textBox_Run.AllowDrop = true;
            this.textBox_Run.BorderStyle = System.Windows.Forms.BorderStyle.None;
            this.textBox_Run.Location = new System.Drawing.Point(6, 30);
            this.textBox_Run.MaxLength = 302767;
            this.textBox_Run.Multiline = true;
            this.textBox_Run.Name = "textBox_Run";
            this.textBox_Run.ScrollBars = System.Windows.Forms.ScrollBars.Vertical;
            this.textBox_Run.Size = new System.Drawing.Size(432, 88);
            this.textBox_Run.TabIndex = 1;
            this.textBox_Run.DragDrop += new System.Windows.Forms.DragEventHandler(this.textBox_Run_DragDrop);
            this.textBox_Run.DragEnter += new System.Windows.Forms.DragEventHandler(this.textBox_Run_DragEnter);
            this.textBox_Run.DragLeave += new System.EventHandler(this.textBox_Run_DragLeave);
            // 
            // textBox_Manager
            // 
            this.textBox_Manager.Location = new System.Drawing.Point(156, 242);
            this.textBox_Manager.Name = "textBox_Manager";
            this.textBox_Manager.Size = new System.Drawing.Size(257, 20);
            this.textBox_Manager.TabIndex = 30;
            // 
            // textBox_NumSpec
            // 
            this.textBox_NumSpec.Location = new System.Drawing.Point(156, 278);
            this.textBox_NumSpec.Name = "textBox_NumSpec";
            this.textBox_NumSpec.Size = new System.Drawing.Size(257, 20);
            this.textBox_NumSpec.TabIndex = 40;
            // 
            // textBox_Client
            // 
            this.textBox_Client.Location = new System.Drawing.Point(156, 316);
            this.textBox_Client.Name = "textBox_Client";
            this.textBox_Client.Size = new System.Drawing.Size(257, 20);
            this.textBox_Client.TabIndex = 50;
            // 
            // comboBox_Cut
            // 
            this.comboBox_Cut.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.comboBox_Cut.ImeMode = System.Windows.Forms.ImeMode.NoControl;
            this.comboBox_Cut.Items.AddRange(new object[] {
            "Лазер",
            "Плазма",
            "Газо-кислородная",
            "HansLaser"});
            this.comboBox_Cut.Location = new System.Drawing.Point(156, 356);
            this.comboBox_Cut.Name = "comboBox_Cut";
            this.comboBox_Cut.Size = new System.Drawing.Size(257, 21);
            this.comboBox_Cut.TabIndex = 60;
            this.comboBox_Cut.SelectedIndexChanged += new System.EventHandler(this.comboBox_Cut_SelectedIndexChanged);
            // 
            // comboBox_SteelGreed
            // 
            this.comboBox_SteelGreed.FormattingEnabled = true;
            this.comboBox_SteelGreed.Items.AddRange(new object[] {
            "Ст3",
            "08ПС",
            "09Г2С",
            "Ст20",
            "Ст45"});
            this.comboBox_SteelGreed.Location = new System.Drawing.Point(156, 398);
            this.comboBox_SteelGreed.Name = "comboBox_SteelGreed";
            this.comboBox_SteelGreed.Size = new System.Drawing.Size(257, 21);
            this.comboBox_SteelGreed.TabIndex = 70;
            // 
            // comboBox_Thickness
            // 
            this.comboBox_Thickness.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.comboBox_Thickness.FormattingEnabled = true;
            this.comboBox_Thickness.Items.AddRange(new object[] {
            "0,5",
            "0,8",
            "1",
            "1,2",
            "1,5",
            "2",
            "2,5",
            "3",
            "4",
            "5",
            "6",
            "8",
            "10",
            "12",
            "14",
            "15",
            "16",
            "18",
            "20",
            "22",
            "25",
            "28",
            "30",
            "32",
            "36",
            "40",
            "45",
            "50",
            "55",
            "60",
            "70",
            "80",
            "90",
            "100",
            "120",
            "200"});
            this.comboBox_Thickness.Location = new System.Drawing.Point(156, 442);
            this.comboBox_Thickness.Name = "comboBox_Thickness";
            this.comboBox_Thickness.Size = new System.Drawing.Size(257, 21);
            this.comboBox_Thickness.TabIndex = 80;
            // 
            // comboBox_Size
            // 
            this.comboBox_Size.FormattingEnabled = true;
            this.comboBox_Size.Items.AddRange(new object[] {
            "1250x2500",
            "1500x3000",
            "1500x6000",
            "2000x6000"});
            this.comboBox_Size.Location = new System.Drawing.Point(156, 483);
            this.comboBox_Size.Name = "comboBox_Size";
            this.comboBox_Size.Size = new System.Drawing.Size(257, 21);
            this.comboBox_Size.TabIndex = 90;
            // 
            // textBox_LengthCut
            // 
            this.textBox_LengthCut.Location = new System.Drawing.Point(156, 526);
            this.textBox_LengthCut.Name = "textBox_LengthCut";
            this.textBox_LengthCut.Size = new System.Drawing.Size(257, 20);
            this.textBox_LengthCut.TabIndex = 100;
            // 
            // textBox_Weight
            // 
            this.textBox_Weight.Location = new System.Drawing.Point(156, 566);
            this.textBox_Weight.Name = "textBox_Weight";
            this.textBox_Weight.Size = new System.Drawing.Size(257, 20);
            this.textBox_Weight.TabIndex = 110;
            // 
            // textBox_SheetCount
            // 
            this.textBox_SheetCount.Location = new System.Drawing.Point(156, 609);
            this.textBox_SheetCount.Name = "textBox_SheetCount";
            this.textBox_SheetCount.Size = new System.Drawing.Size(257, 20);
            this.textBox_SheetCount.TabIndex = 120;
            // 
            // button_Run
            // 
            this.button_Run.BackColor = System.Drawing.SystemColors.ActiveCaption;
            this.button_Run.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.button_Run.Location = new System.Drawing.Point(276, 124);
            this.button_Run.Name = "button_Run";
            this.button_Run.Size = new System.Drawing.Size(160, 27);
            this.button_Run.TabIndex = 20;
            this.button_Run.Text = "Обработать";
            this.button_Run.UseVisualStyleBackColor = false;
            this.button_Run.Click += new System.EventHandler(this.Button_RunClick);
            // 
            // label1
            // 
            this.label1.Location = new System.Drawing.Point(7, 245);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(100, 23);
            this.label1.TabIndex = 7;
            this.label1.Text = "Менеджер";
            // 
            // label2
            // 
            this.label2.Location = new System.Drawing.Point(7, 281);
            this.label2.Name = "label2";
            this.label2.Size = new System.Drawing.Size(100, 23);
            this.label2.TabIndex = 7;
            this.label2.Text = "№ Спецификации";
            // 
            // label3
            // 
            this.label3.Location = new System.Drawing.Point(7, 319);
            this.label3.Name = "label3";
            this.label3.Size = new System.Drawing.Size(100, 23);
            this.label3.TabIndex = 7;
            this.label3.Text = "Заказчик";
            // 
            // label4
            // 
            this.label4.Location = new System.Drawing.Point(7, 359);
            this.label4.Name = "label4";
            this.label4.Size = new System.Drawing.Size(100, 23);
            this.label4.TabIndex = 7;
            this.label4.Text = "Вид резки";
            // 
            // label5
            // 
            this.label5.Location = new System.Drawing.Point(7, 401);
            this.label5.Name = "label5";
            this.label5.Size = new System.Drawing.Size(100, 23);
            this.label5.TabIndex = 7;
            this.label5.Text = "Марка стали";
            // 
            // label6
            // 
            this.label6.Location = new System.Drawing.Point(7, 445);
            this.label6.Name = "label6";
            this.label6.Size = new System.Drawing.Size(100, 23);
            this.label6.TabIndex = 7;
            this.label6.Text = "Толщина";
            // 
            // label7
            // 
            this.label7.Location = new System.Drawing.Point(7, 486);
            this.label7.Name = "label7";
            this.label7.Size = new System.Drawing.Size(100, 23);
            this.label7.TabIndex = 7;
            this.label7.Text = "Размер листа";
            // 
            // label8
            // 
            this.label8.Location = new System.Drawing.Point(7, 523);
            this.label8.Name = "label8";
            this.label8.Size = new System.Drawing.Size(100, 23);
            this.label8.TabIndex = 7;
            this.label8.Text = "Длина реза (м.)";
            // 
            // label9
            // 
            this.label9.Location = new System.Drawing.Point(7, 569);
            this.label9.Name = "label9";
            this.label9.Size = new System.Drawing.Size(100, 23);
            this.label9.TabIndex = 7;
            this.label9.Text = "Вес (кг.)";
            // 
            // label10
            // 
            this.label10.Location = new System.Drawing.Point(7, 612);
            this.label10.Name = "label10";
            this.label10.Size = new System.Drawing.Size(100, 23);
            this.label10.TabIndex = 7;
            this.label10.Text = "Кол-во листов";
            // 
            // panel1
            // 
            this.panel1.BackColor = System.Drawing.SystemColors.ActiveCaption;
            this.panel1.Controls.Add(this.button_Clear);
            this.panel1.Controls.Add(this.label11);
            this.panel1.Controls.Add(this.button_Run);
            this.panel1.Controls.Add(this.textBox_Run);
            this.panel1.Location = new System.Drawing.Point(1, 0);
            this.panel1.Name = "panel1";
            this.panel1.Size = new System.Drawing.Size(448, 160);
            this.panel1.TabIndex = 8;
            // 
            // button_Clear
            // 
            this.button_Clear.BackgroundImage = ((System.Drawing.Image)(resources.GetObject("button_Clear.BackgroundImage")));
            this.button_Clear.BackgroundImageLayout = System.Windows.Forms.ImageLayout.Zoom;
            this.button_Clear.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.button_Clear.ForeColor = System.Drawing.SystemColors.ActiveCaption;
            this.button_Clear.Location = new System.Drawing.Point(226, 121);
            this.button_Clear.Name = "button_Clear";
            this.button_Clear.Size = new System.Drawing.Size(33, 33);
            this.button_Clear.TabIndex = 22;
            this.button_Clear.Text = " ";
            this.button_Clear.UseVisualStyleBackColor = true;
            this.button_Clear.Click += new System.EventHandler(this.Button_ClearClick);
            // 
            // label11
            // 
            this.label11.Location = new System.Drawing.Point(109, 8);
            this.label11.Name = "label11";
            this.label11.Size = new System.Drawing.Size(232, 18);
            this.label11.TabIndex = 21;
            this.label11.Text = "Скопировать текст из Спецификации Блок";
            // 
            // label12
            // 
            this.label12.AutoSize = true;
            this.label12.Location = new System.Drawing.Point(7, 657);
            this.label12.Name = "label12";
            this.label12.Size = new System.Drawing.Size(77, 13);
            this.label12.TabIndex = 141;
            this.label12.Text = "Коэффициент";
            // 
            // textBox_Kf
            // 
            this.textBox_Kf.Location = new System.Drawing.Point(90, 653);
            this.textBox_Kf.MaxLength = 3;
            this.textBox_Kf.Name = "textBox_Kf";
            this.textBox_Kf.Size = new System.Drawing.Size(37, 20);
            this.textBox_Kf.TabIndex = 142;
            this.textBox_Kf.TextChanged += new System.EventHandler(this.textBox_Kf_TextChanged);
            this.textBox_Kf.KeyPress += new System.Windows.Forms.KeyPressEventHandler(this.textBox_Kf_KeyPress);
            // 
            // label_Details
            // 
            this.label_Details.AutoSize = true;
            this.label_Details.Location = new System.Drawing.Point(216, 657);
            this.label_Details.Name = "label_Details";
            this.label_Details.Size = new System.Drawing.Size(0, 13);
            this.label_Details.TabIndex = 143;
            // 
            // label_Punchs
            // 
            this.label_Punchs.AutoSize = true;
            this.label_Punchs.Location = new System.Drawing.Point(350, 657);
            this.label_Punchs.Name = "label_Punchs";
            this.label_Punchs.Size = new System.Drawing.Size(0, 13);
            this.label_Punchs.TabIndex = 144;
            // 
            // label13
            // 
            this.label13.AutoSize = true;
            this.label13.Location = new System.Drawing.Point(156, 657);
            this.label13.Name = "label13";
            this.label13.Size = new System.Drawing.Size(54, 13);
            this.label13.TabIndex = 145;
            this.label13.Text = "Деталей:";
            // 
            // label14
            // 
            this.label14.AutoSize = true;
            this.label14.Location = new System.Drawing.Point(284, 657);
            this.label14.Name = "label14";
            this.label14.Size = new System.Drawing.Size(60, 13);
            this.label14.TabIndex = 146;
            this.label14.Text = "Пробивок:";
            // 
            // textBox_date
            // 
            this.textBox_date.Location = new System.Drawing.Point(62, 180);
            this.textBox_date.Name = "textBox_date";
            this.textBox_date.Size = new System.Drawing.Size(65, 20);
            this.textBox_date.TabIndex = 147;
            // 
            // comboBox_ID_MAT
            // 
            this.comboBox_ID_MAT.AutoCompleteCustomSource.AddRange(new string[] {
            "0",
            "1",
            "2"});
            this.comboBox_ID_MAT.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.comboBox_ID_MAT.Enabled = false;
            this.comboBox_ID_MAT.Font = new System.Drawing.Font("Microsoft Sans Serif", 8.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(204)));
            this.comboBox_ID_MAT.ImeMode = System.Windows.Forms.ImeMode.NoControl;
            this.comboBox_ID_MAT.Items.AddRange(new object[] {
            "0",
            "1",
            "2",
            "-1"});
            this.comboBox_ID_MAT.Location = new System.Drawing.Point(197, 16);
            this.comboBox_ID_MAT.Name = "comboBox_ID_MAT";
            this.comboBox_ID_MAT.Size = new System.Drawing.Size(40, 21);
            this.comboBox_ID_MAT.TabIndex = 148;
            this.comboBox_ID_MAT.SelectedIndexChanged += new System.EventHandler(this.comboBox_ID_MAT_SelectedIndexChanged);
            // 
            // groupBox_ID_MAT
            // 
            this.groupBox_ID_MAT.BackColor = System.Drawing.SystemColors.Control;
            this.groupBox_ID_MAT.Controls.Add(this.label_CUR_GAS);
            this.groupBox_ID_MAT.Controls.Add(this.label_ID_MAT);
            this.groupBox_ID_MAT.Controls.Add(this.comboBox_ID_MAT);
            this.groupBox_ID_MAT.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.groupBox_ID_MAT.Font = new System.Drawing.Font("Microsoft Sans Serif", 8.25F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(204)));
            this.groupBox_ID_MAT.ForeColor = System.Drawing.SystemColors.ActiveCaptionText;
            this.groupBox_ID_MAT.Location = new System.Drawing.Point(156, 167);
            this.groupBox_ID_MAT.Name = "groupBox_ID_MAT";
            this.groupBox_ID_MAT.Size = new System.Drawing.Size(257, 69);
            this.groupBox_ID_MAT.TabIndex = 149;
            this.groupBox_ID_MAT.TabStop = false;
            this.groupBox_ID_MAT.Text = "ID Материала/Газ";
            this.groupBox_ID_MAT.Visible = false;
            // 
            // label_CUR_GAS
            // 
            this.label_CUR_GAS.Font = new System.Drawing.Font("Microsoft Sans Serif", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(204)));
            this.label_CUR_GAS.Location = new System.Drawing.Point(18, 46);
            this.label_CUR_GAS.Name = "label_CUR_GAS";
            this.label_CUR_GAS.Size = new System.Drawing.Size(167, 20);
            this.label_CUR_GAS.TabIndex = 150;
            // 
            // label_ID_MAT
            // 
            this.label_ID_MAT.Font = new System.Drawing.Font("Microsoft Sans Serif", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(204)));
            this.label_ID_MAT.Location = new System.Drawing.Point(18, 16);
            this.label_ID_MAT.Name = "label_ID_MAT";
            this.label_ID_MAT.Size = new System.Drawing.Size(167, 20);
            this.label_ID_MAT.TabIndex = 149;
            // 
            // label15
            // 
            this.label15.Location = new System.Drawing.Point(-2, 178);
            this.label15.Name = "label15";
            this.label15.Size = new System.Drawing.Size(58, 23);
            this.label15.TabIndex = 150;
            this.label15.Text = "Дата";
            this.label15.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            // 
            // Form_NewOrder
            // 
            this.AllowDrop = true;
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.None;
            this.BackgroundImageLayout = System.Windows.Forms.ImageLayout.None;
            this.ClientSize = new System.Drawing.Size(449, 740);
            this.Controls.Add(this.label15);
            this.Controls.Add(this.groupBox_ID_MAT);
            this.Controls.Add(this.textBox_date);
            this.Controls.Add(this.label14);
            this.Controls.Add(this.label13);
            this.Controls.Add(this.label_Punchs);
            this.Controls.Add(this.label_Details);
            this.Controls.Add(this.textBox_Kf);
            this.Controls.Add(this.label12);
            this.Controls.Add(this.panel1);
            this.Controls.Add(this.label10);
            this.Controls.Add(this.label9);
            this.Controls.Add(this.label8);
            this.Controls.Add(this.label7);
            this.Controls.Add(this.label6);
            this.Controls.Add(this.label5);
            this.Controls.Add(this.label4);
            this.Controls.Add(this.label3);
            this.Controls.Add(this.label2);
            this.Controls.Add(this.label1);
            this.Controls.Add(this.comboBox_Size);
            this.Controls.Add(this.comboBox_Thickness);
            this.Controls.Add(this.comboBox_SteelGreed);
            this.Controls.Add(this.comboBox_Cut);
            this.Controls.Add(this.textBox_Client);
            this.Controls.Add(this.textBox_NumSpec);
            this.Controls.Add(this.textBox_SheetCount);
            this.Controls.Add(this.textBox_Weight);
            this.Controls.Add(this.textBox_LengthCut);
            this.Controls.Add(this.textBox_Manager);
            this.Controls.Add(this.button_Cancel);
            this.Controls.Add(this.button_Ok);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedToolWindow;
            this.Name = "Form_NewOrder";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.Text = "Новая заявка";
            this.Load += new System.EventHandler(this.Form1Load);
            this.panel1.ResumeLayout(false);
            this.panel1.PerformLayout();
            this.groupBox_ID_MAT.ResumeLayout(false);
            this.ResumeLayout(false);
            this.PerformLayout();

		}

		private System.Windows.Forms.Button button_Cancel;
		private System.Windows.Forms.Button button_Clear;
		private System.Windows.Forms.Button button_Ok;
		private System.Windows.Forms.Button button_Run;
		public System.Windows.Forms.ComboBox comboBox_Cut;
		public System.Windows.Forms.ComboBox comboBox_Size;
		public System.Windows.Forms.ComboBox comboBox_SteelGreed;
		public System.Windows.Forms.ComboBox comboBox_Thickness;
		private System.Windows.Forms.Label label1;
		private System.Windows.Forms.Label label10;
		private System.Windows.Forms.Label label11;
		private System.Windows.Forms.Label label2;
		private System.Windows.Forms.Label label3;
		private System.Windows.Forms.Label label4;
		private System.Windows.Forms.Label label5;
		private System.Windows.Forms.Label label6;
		private System.Windows.Forms.Label label7;
		private System.Windows.Forms.Label label8;
		private System.Windows.Forms.Label label9;
		private System.Windows.Forms.Panel panel1;
		public System.Windows.Forms.TextBox textBox_Client;
		public System.Windows.Forms.TextBox textBox_LengthCut;
		public System.Windows.Forms.TextBox textBox_Manager;
		public System.Windows.Forms.TextBox textBox_NumSpec;
		private System.Windows.Forms.TextBox textBox_Run;
		public System.Windows.Forms.TextBox textBox_SheetCount;
		public System.Windows.Forms.TextBox textBox_Weight;
        private System.Windows.Forms.Label label12;
        public System.Windows.Forms.TextBox textBox_Kf;
        private System.Windows.Forms.Label label_Details;
        private System.Windows.Forms.Label label_Punchs;
        private System.Windows.Forms.Label label13;
        private System.Windows.Forms.Label label14;
        private System.Windows.Forms.TextBox textBox_date;
        private System.Windows.Forms.Label label_ID_MAT;
        private System.Windows.Forms.Label label_CUR_GAS;
        public System.Windows.Forms.GroupBox groupBox_ID_MAT;
        public System.Windows.Forms.ComboBox comboBox_ID_MAT;
        private System.Windows.Forms.Label label15;
    }
}
