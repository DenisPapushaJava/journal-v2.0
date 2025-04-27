
namespace Jornal
{
	partial class MainForm
	{
		
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
            this.components = new System.ComponentModel.Container();
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(MainForm));
            System.Windows.Forms.DataGridViewCellStyle dataGridViewCellStyle1 = new System.Windows.Forms.DataGridViewCellStyle();
            System.Windows.Forms.DataGridViewCellStyle dataGridViewCellStyle2 = new System.Windows.Forms.DataGridViewCellStyle();
            System.Windows.Forms.DataGridViewCellStyle dataGridViewCellStyle3 = new System.Windows.Forms.DataGridViewCellStyle();
            System.Windows.Forms.DataGridViewCellStyle dataGridViewCellStyle4 = new System.Windows.Forms.DataGridViewCellStyle();
            this.button_Update = new System.Windows.Forms.Button();
            this.button_NewOrder = new System.Windows.Forms.Button();
            this.button_Edit = new System.Windows.Forms.Button();
            this.button_Delete = new System.Windows.Forms.Button();
            this.Grid_Order = new System.Windows.Forms.DataGridView();
            this.contextMenuStrip1 = new System.Windows.Forms.ContextMenuStrip(this.components);
            this.toolStripSeparator1 = new System.Windows.Forms.ToolStripSeparator();
            this.toolStrip_Delete = new System.Windows.Forms.ToolStripSeparator();
            this.редактироватьToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.результатToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.копироватьКомментарийToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.telegramКомментарийToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.удалитьToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.цветToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.вРаботеToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.готовоToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.toolStripMenuItem1 = new System.Windows.Forms.ToolStripMenuItem();
            this.другойЦветToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.снятьВыделениеToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.добавитьБЛЦБToolStripMenuItem1 = new System.Windows.Forms.ToolStripMenuItem();
            this.panel1 = new System.Windows.Forms.Panel();
            this.checkBox_checkColor = new System.Windows.Forms.CheckBox();
            this.button_Clear_Searh = new System.Windows.Forms.Button();
            this.comboBox1 = new System.Windows.Forms.ComboBox();
            this.checkBox_SortChoiceDayON = new System.Windows.Forms.CheckBox();
            this.button_SortChoiceDay = new System.Windows.Forms.Button();
            this.dateTimePicker_DateSort = new System.Windows.Forms.DateTimePicker();
            this.checkBox_Sent = new System.Windows.Forms.CheckBox();
            this.button_Sent = new System.Windows.Forms.Button();
            this.textBox_Searh = new System.Windows.Forms.TextBox();
            this.checkBox_Finished = new System.Windows.Forms.CheckBox();
            this.checkBox_InWork = new System.Windows.Forms.CheckBox();
            this.checkBox_DateDay = new System.Windows.Forms.CheckBox();
            this.button_ClearColor = new System.Windows.Forms.Button();
            this.button_ColorDialog = new System.Windows.Forms.Button();
            this.button_ColorFinish = new System.Windows.Forms.Button();
            this.button_ColorInWork = new System.Windows.Forms.Button();
            this.richTextBox_Result = new System.Windows.Forms.RichTextBox();
            this.button1 = new System.Windows.Forms.Button();
            this.button_Result = new System.Windows.Forms.Button();
            this.button_CopyResult = new System.Windows.Forms.Button();
            this.menuStrip1 = new System.Windows.Forms.MenuStrip();
            this.добавитьБЛЦБToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.telegramКомментарийToolStripMenuItem1 = new System.Windows.Forms.ToolStripMenuItem();
            this.colorDialog = new System.Windows.Forms.ColorDialog();
            this.timer_Color = new System.Windows.Forms.Timer(this.components);
            this.toolStripStatusLabel_Info = new System.Windows.Forms.ToolStripStatusLabel();
            this.statusStrip = new System.Windows.Forms.StatusStrip();
            this.ProgressBar_Info = new System.Windows.Forms.ProgressBar();
            ((System.ComponentModel.ISupportInitialize)(this.Grid_Order)).BeginInit();
            this.contextMenuStrip1.SuspendLayout();
            this.panel1.SuspendLayout();
            this.menuStrip1.SuspendLayout();
            this.statusStrip.SuspendLayout();
            this.SuspendLayout();
            // 
            // button_Update
            // 
            this.button_Update.BackColor = System.Drawing.SystemColors.ActiveCaption;
            this.button_Update.BackgroundImage = ((System.Drawing.Image)(resources.GetObject("button_Update.BackgroundImage")));
            this.button_Update.BackgroundImageLayout = System.Windows.Forms.ImageLayout.Center;
            this.button_Update.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.button_Update.ForeColor = System.Drawing.SystemColors.ActiveCaption;
            this.button_Update.Location = new System.Drawing.Point(8, 31);
            this.button_Update.Name = "button_Update";
            this.button_Update.Size = new System.Drawing.Size(57, 55);
            this.button_Update.TabIndex = 0;
            this.button_Update.Text = " ";
            this.button_Update.UseVisualStyleBackColor = false;
            this.button_Update.Click += new System.EventHandler(this.Button_UpdateClick);
            this.button_Update.MouseEnter += new System.EventHandler(this.button_Update_MouseEnter);
            // 
            // button_NewOrder
            // 
            this.button_NewOrder.BackColor = System.Drawing.SystemColors.ActiveCaption;
            this.button_NewOrder.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.button_NewOrder.Location = new System.Drawing.Point(71, 36);
            this.button_NewOrder.Name = "button_NewOrder";
            this.button_NewOrder.Size = new System.Drawing.Size(88, 45);
            this.button_NewOrder.TabIndex = 1;
            this.button_NewOrder.Text = "Новая заявка";
            this.button_NewOrder.UseVisualStyleBackColor = false;
            this.button_NewOrder.Click += new System.EventHandler(this.Button_NewOrderClick);
            // 
            // button_Edit
            // 
            this.button_Edit.BackColor = System.Drawing.SystemColors.ActiveCaption;
            this.button_Edit.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.button_Edit.Location = new System.Drawing.Point(166, 36);
            this.button_Edit.Name = "button_Edit";
            this.button_Edit.Size = new System.Drawing.Size(101, 45);
            this.button_Edit.TabIndex = 2;
            this.button_Edit.Text = "Редактировать";
            this.button_Edit.UseVisualStyleBackColor = false;
            this.button_Edit.Click += new System.EventHandler(this.Button_EditClick);
            // 
            // button_Delete
            // 
            this.button_Delete.BackColor = System.Drawing.SystemColors.ActiveCaption;
            this.button_Delete.BackgroundImageLayout = System.Windows.Forms.ImageLayout.None;
            this.button_Delete.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.button_Delete.Location = new System.Drawing.Point(273, 36);
            this.button_Delete.Name = "button_Delete";
            this.button_Delete.Size = new System.Drawing.Size(91, 45);
            this.button_Delete.TabIndex = 3;
            this.button_Delete.Text = "Удалить";
            this.button_Delete.UseVisualStyleBackColor = false;
            this.button_Delete.Click += new System.EventHandler(this.Button_DeleteClick);
            // 
            // Grid_Order
            // 
            this.Grid_Order.AccessibleRole = System.Windows.Forms.AccessibleRole.Window;
            this.Grid_Order.AllowUserToAddRows = false;
            this.Grid_Order.AllowUserToDeleteRows = false;
            this.Grid_Order.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.Grid_Order.AutoSizeColumnsMode = System.Windows.Forms.DataGridViewAutoSizeColumnsMode.Fill;
            this.Grid_Order.AutoSizeRowsMode = System.Windows.Forms.DataGridViewAutoSizeRowsMode.AllCells;
            this.Grid_Order.BackgroundColor = System.Drawing.SystemColors.Menu;
            this.Grid_Order.CellBorderStyle = System.Windows.Forms.DataGridViewCellBorderStyle.RaisedHorizontal;
            this.Grid_Order.ColumnHeadersBorderStyle = System.Windows.Forms.DataGridViewHeaderBorderStyle.Sunken;
            dataGridViewCellStyle1.Alignment = System.Windows.Forms.DataGridViewContentAlignment.TopCenter;
            dataGridViewCellStyle1.BackColor = System.Drawing.SystemColors.Control;
            dataGridViewCellStyle1.Font = new System.Drawing.Font("Microsoft Sans Serif", 9.75F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            dataGridViewCellStyle1.ForeColor = System.Drawing.SystemColors.WindowText;
            dataGridViewCellStyle1.SelectionBackColor = System.Drawing.Color.Transparent;
            dataGridViewCellStyle1.SelectionForeColor = System.Drawing.SystemColors.HighlightText;
            dataGridViewCellStyle1.WrapMode = System.Windows.Forms.DataGridViewTriState.True;
            this.Grid_Order.ColumnHeadersDefaultCellStyle = dataGridViewCellStyle1;
            this.Grid_Order.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            this.Grid_Order.ContextMenuStrip = this.contextMenuStrip1;
            this.Grid_Order.Cursor = System.Windows.Forms.Cursors.Hand;
            dataGridViewCellStyle2.Alignment = System.Windows.Forms.DataGridViewContentAlignment.MiddleLeft;
            dataGridViewCellStyle2.BackColor = System.Drawing.SystemColors.InactiveBorder;
            dataGridViewCellStyle2.Font = new System.Drawing.Font("Microsoft Sans Serif", 10F);
            dataGridViewCellStyle2.ForeColor = System.Drawing.SystemColors.ActiveCaptionText;
            dataGridViewCellStyle2.SelectionBackColor = System.Drawing.Color.Transparent;
            dataGridViewCellStyle2.SelectionForeColor = System.Drawing.Color.White;
            dataGridViewCellStyle2.WrapMode = System.Windows.Forms.DataGridViewTriState.False;
            this.Grid_Order.DefaultCellStyle = dataGridViewCellStyle2;
            this.Grid_Order.GridColor = System.Drawing.SystemColors.Control;
            this.Grid_Order.Location = new System.Drawing.Point(12, 152);
            this.Grid_Order.Name = "Grid_Order";
            this.Grid_Order.ReadOnly = true;
            this.Grid_Order.RowHeadersBorderStyle = System.Windows.Forms.DataGridViewHeaderBorderStyle.None;
            dataGridViewCellStyle3.Alignment = System.Windows.Forms.DataGridViewContentAlignment.MiddleCenter;
            dataGridViewCellStyle3.BackColor = System.Drawing.SystemColors.Control;
            dataGridViewCellStyle3.Font = new System.Drawing.Font("Microsoft Sans Serif", 8.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            dataGridViewCellStyle3.ForeColor = System.Drawing.SystemColors.WindowText;
            dataGridViewCellStyle3.SelectionBackColor = System.Drawing.Color.Transparent;
            dataGridViewCellStyle3.SelectionForeColor = System.Drawing.SystemColors.HighlightText;
            dataGridViewCellStyle3.WrapMode = System.Windows.Forms.DataGridViewTriState.True;
            this.Grid_Order.RowHeadersDefaultCellStyle = dataGridViewCellStyle3;
            this.Grid_Order.RowHeadersVisible = false;
            dataGridViewCellStyle4.Alignment = System.Windows.Forms.DataGridViewContentAlignment.MiddleCenter;
            this.Grid_Order.RowsDefaultCellStyle = dataGridViewCellStyle4;
            this.Grid_Order.SelectionMode = System.Windows.Forms.DataGridViewSelectionMode.FullRowSelect;
            this.Grid_Order.Size = new System.Drawing.Size(1812, 333);
            this.Grid_Order.TabIndex = 4;
            this.Grid_Order.VirtualMode = true;
            this.Grid_Order.SelectionChanged += new System.EventHandler(this.Grid_Order_SelectionChanged);
            this.Grid_Order.MouseDown += new System.Windows.Forms.MouseEventHandler(this.Grid_OrderMouseDown);
            // 
            // contextMenuStrip1
            // 
            this.contextMenuStrip1.BackColor = System.Drawing.SystemColors.GradientInactiveCaption;
            this.contextMenuStrip1.ImageScalingSize = new System.Drawing.Size(20, 20);
            this.contextMenuStrip1.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.toolStripSeparator1,
            this.toolStrip_Delete,
            this.редактироватьToolStripMenuItem,
            this.результатToolStripMenuItem,
            this.копироватьКомментарийToolStripMenuItem,
            this.telegramКомментарийToolStripMenuItem,
            this.удалитьToolStripMenuItem,
            this.цветToolStripMenuItem,
            this.добавитьБЛЦБToolStripMenuItem1});
            this.contextMenuStrip1.Name = "contextMenuStrip1";
            this.contextMenuStrip1.Size = new System.Drawing.Size(223, 198);
            // 
            // toolStripSeparator1
            // 
            this.toolStripSeparator1.Name = "toolStripSeparator1";
            this.toolStripSeparator1.Size = new System.Drawing.Size(219, 6);
            // 
            // toolStrip_Delete
            // 
            this.toolStrip_Delete.Name = "toolStrip_Delete";
            this.toolStrip_Delete.Size = new System.Drawing.Size(219, 6);
            // 
            // редактироватьToolStripMenuItem
            // 
            this.редактироватьToolStripMenuItem.BackColor = System.Drawing.SystemColors.GradientInactiveCaption;
            this.редактироватьToolStripMenuItem.BackgroundImageLayout = System.Windows.Forms.ImageLayout.None;
            this.редактироватьToolStripMenuItem.Image = ((System.Drawing.Image)(resources.GetObject("редактироватьToolStripMenuItem.Image")));
            this.редактироватьToolStripMenuItem.ImageAlign = System.Drawing.ContentAlignment.BottomRight;
            this.редактироватьToolStripMenuItem.ImageTransparentColor = System.Drawing.Color.LightGray;
            this.редактироватьToolStripMenuItem.Name = "редактироватьToolStripMenuItem";
            this.редактироватьToolStripMenuItem.Size = new System.Drawing.Size(222, 26);
            this.редактироватьToolStripMenuItem.Text = "Редактировать";
            this.редактироватьToolStripMenuItem.TextImageRelation = System.Windows.Forms.TextImageRelation.TextAboveImage;
            this.редактироватьToolStripMenuItem.Click += new System.EventHandler(this.РедактироватьToolStripMenuItemClick);
            // 
            // результатToolStripMenuItem
            // 
            this.результатToolStripMenuItem.Image = ((System.Drawing.Image)(resources.GetObject("результатToolStripMenuItem.Image")));
            this.результатToolStripMenuItem.Name = "результатToolStripMenuItem";
            this.результатToolStripMenuItem.Size = new System.Drawing.Size(222, 26);
            this.результатToolStripMenuItem.Text = "Результат";
            this.результатToolStripMenuItem.Click += new System.EventHandler(this.РезультатToolStripMenuItemClick);
            // 
            // копироватьКомментарийToolStripMenuItem
            // 
            this.копироватьКомментарийToolStripMenuItem.Image = ((System.Drawing.Image)(resources.GetObject("копироватьКомментарийToolStripMenuItem.Image")));
            this.копироватьКомментарийToolStripMenuItem.Name = "копироватьКомментарийToolStripMenuItem";
            this.копироватьКомментарийToolStripMenuItem.Size = new System.Drawing.Size(222, 26);
            this.копироватьКомментарийToolStripMenuItem.Text = "Копировать комментарий";
            this.копироватьКомментарийToolStripMenuItem.Click += new System.EventHandler(this.КопироватьКомментарийToolStripMenuItem_Click);
            // 
            // telegramКомментарийToolStripMenuItem
            // 
            this.telegramКомментарийToolStripMenuItem.Image = ((System.Drawing.Image)(resources.GetObject("telegramКомментарийToolStripMenuItem.Image")));
            this.telegramКомментарийToolStripMenuItem.Name = "telegramКомментарийToolStripMenuItem";
            this.telegramКомментарийToolStripMenuItem.Size = new System.Drawing.Size(222, 26);
            this.telegramКомментарийToolStripMenuItem.Text = "Telegram комментарий";
            this.telegramКомментарийToolStripMenuItem.Click += new System.EventHandler(this.TelegramКомментарийToolStripMenuItem_Click);
            // 
            // удалитьToolStripMenuItem
            // 
            this.удалитьToolStripMenuItem.Image = ((System.Drawing.Image)(resources.GetObject("удалитьToolStripMenuItem.Image")));
            this.удалитьToolStripMenuItem.Name = "удалитьToolStripMenuItem";
            this.удалитьToolStripMenuItem.Size = new System.Drawing.Size(222, 26);
            this.удалитьToolStripMenuItem.Text = "Удалить";
            this.удалитьToolStripMenuItem.Click += new System.EventHandler(this.УдалитьToolStripMenuItemClick);
            // 
            // цветToolStripMenuItem
            // 
            this.цветToolStripMenuItem.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.вРаботеToolStripMenuItem,
            this.готовоToolStripMenuItem,
            this.toolStripMenuItem1,
            this.другойЦветToolStripMenuItem,
            this.снятьВыделениеToolStripMenuItem});
            this.цветToolStripMenuItem.Image = ((System.Drawing.Image)(resources.GetObject("цветToolStripMenuItem.Image")));
            this.цветToolStripMenuItem.Name = "цветToolStripMenuItem";
            this.цветToolStripMenuItem.Size = new System.Drawing.Size(222, 26);
            this.цветToolStripMenuItem.Text = "Цвет";
            // 
            // вРаботеToolStripMenuItem
            // 
            this.вРаботеToolStripMenuItem.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(255)))), ((int)(((byte)(128)))), ((int)(((byte)(128)))));
            this.вРаботеToolStripMenuItem.Margin = new System.Windows.Forms.Padding(2);
            this.вРаботеToolStripMenuItem.Name = "вРаботеToolStripMenuItem";
            this.вРаботеToolStripMenuItem.Size = new System.Drawing.Size(169, 22);
            this.вРаботеToolStripMenuItem.Text = "В работе";
            this.вРаботеToolStripMenuItem.Click += new System.EventHandler(this.ВРаботеToolStripMenuItemClick);
            // 
            // готовоToolStripMenuItem
            // 
            this.готовоToolStripMenuItem.BackColor = System.Drawing.Color.DarkSeaGreen;
            this.готовоToolStripMenuItem.Margin = new System.Windows.Forms.Padding(2);
            this.готовоToolStripMenuItem.Name = "готовоToolStripMenuItem";
            this.готовоToolStripMenuItem.Size = new System.Drawing.Size(169, 22);
            this.готовоToolStripMenuItem.Text = "Готово";
            this.готовоToolStripMenuItem.Click += new System.EventHandler(this.ГотовоToolStripMenuItemClick);
            // 
            // toolStripMenuItem1
            // 
            this.toolStripMenuItem1.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(128)))), ((int)(((byte)(128)))), ((int)(((byte)(255)))));
            this.toolStripMenuItem1.Margin = new System.Windows.Forms.Padding(2);
            this.toolStripMenuItem1.Name = "toolStripMenuItem1";
            this.toolStripMenuItem1.Size = new System.Drawing.Size(169, 22);
            this.toolStripMenuItem1.Text = "Отгружено";
            this.toolStripMenuItem1.Click += new System.EventHandler(this.ToolStripMenuItem1Click);
            // 
            // другойЦветToolStripMenuItem
            // 
            this.другойЦветToolStripMenuItem.Name = "другойЦветToolStripMenuItem";
            this.другойЦветToolStripMenuItem.Size = new System.Drawing.Size(169, 22);
            this.другойЦветToolStripMenuItem.Text = "Другой цвет...";
            this.другойЦветToolStripMenuItem.Click += new System.EventHandler(this.ДругойЦветToolStripMenuItemClick);
            // 
            // снятьВыделениеToolStripMenuItem
            // 
            this.снятьВыделениеToolStripMenuItem.Name = "снятьВыделениеToolStripMenuItem";
            this.снятьВыделениеToolStripMenuItem.Size = new System.Drawing.Size(169, 22);
            this.снятьВыделениеToolStripMenuItem.Text = "Снять выделение";
            this.снятьВыделениеToolStripMenuItem.Click += new System.EventHandler(this.СнятьВыделениеToolStripMenuItemClick);
            // 
            // добавитьБЛЦБToolStripMenuItem1
            // 
            this.добавитьБЛЦБToolStripMenuItem1.Image = ((System.Drawing.Image)(resources.GetObject("добавитьБЛЦБToolStripMenuItem1.Image")));
            this.добавитьБЛЦБToolStripMenuItem1.Name = "добавитьБЛЦБToolStripMenuItem1";
            this.добавитьБЛЦБToolStripMenuItem1.Size = new System.Drawing.Size(222, 26);
            this.добавитьБЛЦБToolStripMenuItem1.Text = "Добавить БЛЦБ";
            this.добавитьБЛЦБToolStripMenuItem1.Click += new System.EventHandler(this.ДобавитьБЛЦБToolStripMenuItem1Click);
            // 
            // panel1
            // 
            this.panel1.AllowDrop = true;
            this.panel1.BackColor = System.Drawing.SystemColors.ActiveCaption;
            this.panel1.Controls.Add(this.checkBox_checkColor);
            this.panel1.Controls.Add(this.button_Clear_Searh);
            this.panel1.Controls.Add(this.comboBox1);
            this.panel1.Controls.Add(this.checkBox_SortChoiceDayON);
            this.panel1.Controls.Add(this.button_SortChoiceDay);
            this.panel1.Controls.Add(this.dateTimePicker_DateSort);
            this.panel1.Controls.Add(this.checkBox_Sent);
            this.panel1.Controls.Add(this.button_Sent);
            this.panel1.Controls.Add(this.textBox_Searh);
            this.panel1.Controls.Add(this.checkBox_Finished);
            this.panel1.Controls.Add(this.checkBox_InWork);
            this.panel1.Controls.Add(this.checkBox_DateDay);
            this.panel1.Controls.Add(this.button_ClearColor);
            this.panel1.Controls.Add(this.button_ColorDialog);
            this.panel1.Controls.Add(this.button_ColorFinish);
            this.panel1.Controls.Add(this.button_ColorInWork);
            this.panel1.Controls.Add(this.richTextBox_Result);
            this.panel1.Controls.Add(this.button1);
            this.panel1.Controls.Add(this.button_Result);
            this.panel1.Controls.Add(this.button_CopyResult);
            this.panel1.Controls.Add(this.button_Edit);
            this.panel1.Controls.Add(this.button_Delete);
            this.panel1.Controls.Add(this.button_NewOrder);
            this.panel1.Controls.Add(this.button_Update);
            this.panel1.Controls.Add(this.menuStrip1);
            this.panel1.Dock = System.Windows.Forms.DockStyle.Top;
            this.panel1.ImeMode = System.Windows.Forms.ImeMode.NoControl;
            this.panel1.Location = new System.Drawing.Point(0, 0);
            this.panel1.Name = "panel1";
            this.panel1.Size = new System.Drawing.Size(1834, 144);
            this.panel1.TabIndex = 5;
            // 
            // checkBox_checkColor
            // 
            this.checkBox_checkColor.AutoSize = true;
            this.checkBox_checkColor.Location = new System.Drawing.Point(1686, 114);
            this.checkBox_checkColor.Name = "checkBox_checkColor";
            this.checkBox_checkColor.Size = new System.Drawing.Size(113, 17);
            this.checkBox_checkColor.TabIndex = 30;
            this.checkBox_checkColor.Text = "узнать код цвета";
            this.checkBox_checkColor.UseVisualStyleBackColor = true;
            // 
            // button_Clear_Searh
            // 
            this.button_Clear_Searh.BackColor = System.Drawing.Color.LightGray;
            this.button_Clear_Searh.BackgroundImage = ((System.Drawing.Image)(resources.GetObject("button_Clear_Searh.BackgroundImage")));
            this.button_Clear_Searh.BackgroundImageLayout = System.Windows.Forms.ImageLayout.Zoom;
            this.button_Clear_Searh.Location = new System.Drawing.Point(1352, 82);
            this.button_Clear_Searh.Name = "button_Clear_Searh";
            this.button_Clear_Searh.Size = new System.Drawing.Size(20, 20);
            this.button_Clear_Searh.TabIndex = 29;
            this.button_Clear_Searh.UseVisualStyleBackColor = false;
            this.button_Clear_Searh.Click += new System.EventHandler(this.Button_Clear_Searh_Click);
            // 
            // comboBox1
            // 
            this.comboBox1.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.comboBox1.FormattingEnabled = true;
            this.comboBox1.Items.AddRange(new object[] {
            "Менеджер",
            "Заказчик",
            "Спецификация",
            "БЛЦБ",
            "Толщина"});
            this.comboBox1.Location = new System.Drawing.Point(1258, 109);
            this.comboBox1.Name = "comboBox1";
            this.comboBox1.Size = new System.Drawing.Size(112, 21);
            this.comboBox1.TabIndex = 28;
            this.comboBox1.SelectedIndexChanged += new System.EventHandler(this.ComboBox1_SelectedIndexChanged);
            // 
            // checkBox_SortChoiceDayON
            // 
            this.checkBox_SortChoiceDayON.Location = new System.Drawing.Point(229, 91);
            this.checkBox_SortChoiceDayON.Name = "checkBox_SortChoiceDayON";
            this.checkBox_SortChoiceDayON.Size = new System.Drawing.Size(39, 24);
            this.checkBox_SortChoiceDayON.TabIndex = 27;
            this.checkBox_SortChoiceDayON.Text = "checkBox_SortChoiceDayON";
            this.checkBox_SortChoiceDayON.UseVisualStyleBackColor = true;
            this.checkBox_SortChoiceDayON.Visible = false;
            // 
            // button_SortChoiceDay
            // 
            this.button_SortChoiceDay.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.button_SortChoiceDay.Location = new System.Drawing.Point(167, 91);
            this.button_SortChoiceDay.Name = "button_SortChoiceDay";
            this.button_SortChoiceDay.Size = new System.Drawing.Size(56, 24);
            this.button_SortChoiceDay.TabIndex = 26;
            this.button_SortChoiceDay.Text = "Найти";
            this.button_SortChoiceDay.UseVisualStyleBackColor = true;
            this.button_SortChoiceDay.Click += new System.EventHandler(this.Button_SortChoiceDayClick);
            // 
            // dateTimePicker_DateSort
            // 
            this.dateTimePicker_DateSort.CalendarTrailingForeColor = System.Drawing.SystemColors.Desktop;
            this.dateTimePicker_DateSort.Cursor = System.Windows.Forms.Cursors.Hand;
            this.dateTimePicker_DateSort.Font = new System.Drawing.Font("Microsoft Sans Serif", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(204)));
            this.dateTimePicker_DateSort.Format = System.Windows.Forms.DateTimePickerFormat.Short;
            this.dateTimePicker_DateSort.Location = new System.Drawing.Point(71, 91);
            this.dateTimePicker_DateSort.Name = "dateTimePicker_DateSort";
            this.dateTimePicker_DateSort.Size = new System.Drawing.Size(88, 22);
            this.dateTimePicker_DateSort.TabIndex = 25;
            // 
            // checkBox_Sent
            // 
            this.checkBox_Sent.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.checkBox_Sent.Font = new System.Drawing.Font("Microsoft Sans Serif", 8.25F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(204)));
            this.checkBox_Sent.ForeColor = System.Drawing.SystemColors.Highlight;
            this.checkBox_Sent.Location = new System.Drawing.Point(339, 114);
            this.checkBox_Sent.Name = "checkBox_Sent";
            this.checkBox_Sent.Size = new System.Drawing.Size(91, 24);
            this.checkBox_Sent.TabIndex = 24;
            this.checkBox_Sent.Text = "Отгружено";
            this.checkBox_Sent.UseVisualStyleBackColor = true;
            this.checkBox_Sent.CheckedChanged += new System.EventHandler(this.CheckBox_SentCheckedChanged);
            // 
            // button_Sent
            // 
            this.button_Sent.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(128)))), ((int)(((byte)(128)))), ((int)(((byte)(255)))));
            this.button_Sent.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.button_Sent.Font = new System.Drawing.Font("Microsoft Sans Serif", 8.25F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(204)));
            this.button_Sent.ForeColor = System.Drawing.SystemColors.ButtonHighlight;
            this.button_Sent.Location = new System.Drawing.Point(1615, 35);
            this.button_Sent.Name = "button_Sent";
            this.button_Sent.Size = new System.Drawing.Size(81, 46);
            this.button_Sent.TabIndex = 23;
            this.button_Sent.Text = "Отгружено";
            this.button_Sent.UseVisualStyleBackColor = false;
            this.button_Sent.Click += new System.EventHandler(this.Button_SentClick);
            // 
            // textBox_Searh
            // 
            this.textBox_Searh.Location = new System.Drawing.Point(1258, 82);
            this.textBox_Searh.Name = "textBox_Searh";
            this.textBox_Searh.Size = new System.Drawing.Size(113, 20);
            this.textBox_Searh.TabIndex = 21;
            this.textBox_Searh.TextChanged += new System.EventHandler(this.TextBox_Searh_TextChanged);
            this.textBox_Searh.KeyDown += new System.Windows.Forms.KeyEventHandler(this.TextBox_SearhKeyDown);
            this.textBox_Searh.KeyPress += new System.Windows.Forms.KeyPressEventHandler(this.TextBox_Searh_KeyPress);
            // 
            // checkBox_Finished
            // 
            this.checkBox_Finished.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.checkBox_Finished.Font = new System.Drawing.Font("Microsoft Sans Serif", 8.25F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(204)));
            this.checkBox_Finished.ForeColor = System.Drawing.Color.Green;
            this.checkBox_Finished.Location = new System.Drawing.Point(261, 114);
            this.checkBox_Finished.Name = "checkBox_Finished";
            this.checkBox_Finished.Size = new System.Drawing.Size(72, 24);
            this.checkBox_Finished.TabIndex = 19;
            this.checkBox_Finished.Text = "Готово";
            this.checkBox_Finished.UseVisualStyleBackColor = true;
            this.checkBox_Finished.CheckedChanged += new System.EventHandler(this.CheckBox_FinishedCheckedChanged);
            // 
            // checkBox_InWork
            // 
            this.checkBox_InWork.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.checkBox_InWork.Font = new System.Drawing.Font("Microsoft Sans Serif", 8.25F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(204)));
            this.checkBox_InWork.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(192)))), ((int)(((byte)(0)))), ((int)(((byte)(0)))));
            this.checkBox_InWork.Location = new System.Drawing.Point(175, 113);
            this.checkBox_InWork.Name = "checkBox_InWork";
            this.checkBox_InWork.Size = new System.Drawing.Size(80, 25);
            this.checkBox_InWork.TabIndex = 18;
            this.checkBox_InWork.Text = "В работе";
            this.checkBox_InWork.UseVisualStyleBackColor = true;
            this.checkBox_InWork.CheckedChanged += new System.EventHandler(this.CheckBox_InWorkCheckedChanged);
            // 
            // checkBox_DateDay
            // 
            this.checkBox_DateDay.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.checkBox_DateDay.Font = new System.Drawing.Font("Microsoft Sans Serif", 8.25F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(204)));
            this.checkBox_DateDay.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(64)))), ((int)(((byte)(64)))), ((int)(((byte)(64)))));
            this.checkBox_DateDay.Location = new System.Drawing.Point(8, 112);
            this.checkBox_DateDay.Name = "checkBox_DateDay";
            this.checkBox_DateDay.Size = new System.Drawing.Size(72, 24);
            this.checkBox_DateDay.TabIndex = 17;
            this.checkBox_DateDay.Text = "Сегодня";
            this.checkBox_DateDay.UseVisualStyleBackColor = true;
            this.checkBox_DateDay.CheckedChanged += new System.EventHandler(this.CheckBox_DateDayCheckedChanged);
            // 
            // button_ClearColor
            // 
            this.button_ClearColor.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.button_ClearColor.Location = new System.Drawing.Point(1441, 97);
            this.button_ClearColor.Name = "button_ClearColor";
            this.button_ClearColor.Size = new System.Drawing.Size(168, 34);
            this.button_ClearColor.TabIndex = 16;
            this.button_ClearColor.Text = "Снять выделение";
            this.button_ClearColor.UseVisualStyleBackColor = true;
            this.button_ClearColor.Click += new System.EventHandler(this.Button_ClearColorClick);
            // 
            // button_ColorDialog
            // 
            this.button_ColorDialog.BackColor = System.Drawing.SystemColors.ActiveCaption;
            this.button_ColorDialog.BackgroundImage = ((System.Drawing.Image)(resources.GetObject("button_ColorDialog.BackgroundImage")));
            this.button_ColorDialog.BackgroundImageLayout = System.Windows.Forms.ImageLayout.Zoom;
            this.button_ColorDialog.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.button_ColorDialog.ForeColor = System.Drawing.SystemColors.ActiveCaption;
            this.button_ColorDialog.Location = new System.Drawing.Point(1628, 93);
            this.button_ColorDialog.Name = "button_ColorDialog";
            this.button_ColorDialog.Size = new System.Drawing.Size(52, 40);
            this.button_ColorDialog.TabIndex = 15;
            this.button_ColorDialog.UseVisualStyleBackColor = false;
            this.button_ColorDialog.Click += new System.EventHandler(this.Button_ColorDialogClick);
            // 
            // button_ColorFinish
            // 
            this.button_ColorFinish.BackColor = System.Drawing.Color.DarkSeaGreen;
            this.button_ColorFinish.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.button_ColorFinish.Font = new System.Drawing.Font("Microsoft Sans Serif", 8.25F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(204)));
            this.button_ColorFinish.ForeColor = System.Drawing.SystemColors.ButtonHighlight;
            this.button_ColorFinish.Location = new System.Drawing.Point(1528, 35);
            this.button_ColorFinish.Name = "button_ColorFinish";
            this.button_ColorFinish.Size = new System.Drawing.Size(81, 46);
            this.button_ColorFinish.TabIndex = 14;
            this.button_ColorFinish.Text = "Готово";
            this.button_ColorFinish.UseVisualStyleBackColor = false;
            this.button_ColorFinish.Click += new System.EventHandler(this.Button_ColorFinishClick);
            // 
            // button_ColorInWork
            // 
            this.button_ColorInWork.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(255)))), ((int)(((byte)(128)))), ((int)(((byte)(128)))));
            this.button_ColorInWork.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.button_ColorInWork.Font = new System.Drawing.Font("Microsoft Sans Serif", 8.25F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(204)));
            this.button_ColorInWork.ForeColor = System.Drawing.SystemColors.ButtonHighlight;
            this.button_ColorInWork.Location = new System.Drawing.Point(1441, 35);
            this.button_ColorInWork.Name = "button_ColorInWork";
            this.button_ColorInWork.Size = new System.Drawing.Size(81, 46);
            this.button_ColorInWork.TabIndex = 13;
            this.button_ColorInWork.Text = "В работе";
            this.button_ColorInWork.UseVisualStyleBackColor = false;
            this.button_ColorInWork.Click += new System.EventHandler(this.Button_ColorInWorkClick);
            // 
            // richTextBox_Result
            // 
            this.richTextBox_Result.BackColor = System.Drawing.SystemColors.InactiveBorder;
            this.richTextBox_Result.Font = new System.Drawing.Font("Times New Roman", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(204)));
            this.richTextBox_Result.ForeColor = System.Drawing.Color.Red;
            this.richTextBox_Result.Location = new System.Drawing.Point(495, 31);
            this.richTextBox_Result.Name = "richTextBox_Result";
            this.richTextBox_Result.ScrollBars = System.Windows.Forms.RichTextBoxScrollBars.ForcedBoth;
            this.richTextBox_Result.Size = new System.Drawing.Size(757, 99);
            this.richTextBox_Result.TabIndex = 10;
            this.richTextBox_Result.Text = "";
            this.richTextBox_Result.WordWrap = false;
            // 
            // button1
            // 
            this.button1.BackgroundImage = ((System.Drawing.Image)(resources.GetObject("button1.BackgroundImage")));
            this.button1.BackgroundImageLayout = System.Windows.Forms.ImageLayout.Zoom;
            this.button1.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.button1.ForeColor = System.Drawing.SystemColors.ActiveCaption;
            this.button1.Location = new System.Drawing.Point(1325, 36);
            this.button1.Name = "button1";
            this.button1.Size = new System.Drawing.Size(47, 45);
            this.button1.TabIndex = 8;
            this.button1.Text = " ";
            this.button1.UseVisualStyleBackColor = true;
            this.button1.Click += new System.EventHandler(this.Button1Click);
            // 
            // button_Result
            // 
            this.button_Result.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.button_Result.Location = new System.Drawing.Point(387, 36);
            this.button_Result.Name = "button_Result";
            this.button_Result.Size = new System.Drawing.Size(88, 44);
            this.button_Result.TabIndex = 6;
            this.button_Result.Text = "Результат";
            this.button_Result.UseVisualStyleBackColor = true;
            this.button_Result.Click += new System.EventHandler(this.Button_ResultClick);
            // 
            // button_CopyResult
            // 
            this.button_CopyResult.BackColor = System.Drawing.SystemColors.ActiveCaption;
            this.button_CopyResult.BackgroundImage = ((System.Drawing.Image)(resources.GetObject("button_CopyResult.BackgroundImage")));
            this.button_CopyResult.BackgroundImageLayout = System.Windows.Forms.ImageLayout.Stretch;
            this.button_CopyResult.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.button_CopyResult.ForeColor = System.Drawing.SystemColors.ActiveCaption;
            this.button_CopyResult.Location = new System.Drawing.Point(1271, 36);
            this.button_CopyResult.Name = "button_CopyResult";
            this.button_CopyResult.Size = new System.Drawing.Size(45, 45);
            this.button_CopyResult.TabIndex = 7;
            this.button_CopyResult.Text = " ";
            this.button_CopyResult.UseVisualStyleBackColor = false;
            this.button_CopyResult.Click += new System.EventHandler(this.Button_CopyResultClick);
            // 
            // menuStrip1
            // 
            this.menuStrip1.ImageScalingSize = new System.Drawing.Size(20, 20);
            this.menuStrip1.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.добавитьБЛЦБToolStripMenuItem,
            this.telegramКомментарийToolStripMenuItem1});
            this.menuStrip1.Location = new System.Drawing.Point(0, 0);
            this.menuStrip1.Name = "menuStrip1";
            this.menuStrip1.Size = new System.Drawing.Size(1834, 28);
            this.menuStrip1.TabIndex = 12;
            this.menuStrip1.Text = "menuStrip1";
            // 
            // добавитьБЛЦБToolStripMenuItem
            // 
            this.добавитьБЛЦБToolStripMenuItem.Font = new System.Drawing.Font("Segoe UI", 8.25F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(204)));
            this.добавитьБЛЦБToolStripMenuItem.Image = ((System.Drawing.Image)(resources.GetObject("добавитьБЛЦБToolStripMenuItem.Image")));
            this.добавитьБЛЦБToolStripMenuItem.Name = "добавитьБЛЦБToolStripMenuItem";
            this.добавитьБЛЦБToolStripMenuItem.Size = new System.Drawing.Size(126, 24);
            this.добавитьБЛЦБToolStripMenuItem.Text = "Добавить БЛЦБ";
            this.добавитьБЛЦБToolStripMenuItem.Click += new System.EventHandler(this.ДобавитьБЛЦБToolStripMenuItemClick);
            // 
            // telegramКомментарийToolStripMenuItem1
            // 
            this.telegramКомментарийToolStripMenuItem1.Image = ((System.Drawing.Image)(resources.GetObject("telegramКомментарийToolStripMenuItem1.Image")));
            this.telegramКомментарийToolStripMenuItem1.Name = "telegramКомментарийToolStripMenuItem1";
            this.telegramКомментарийToolStripMenuItem1.Size = new System.Drawing.Size(166, 24);
            this.telegramКомментарийToolStripMenuItem1.Text = "Telegram комментарий";
            this.telegramКомментарийToolStripMenuItem1.Click += new System.EventHandler(this.TelegramКомментарийToolStripMenuItem1_Click);
            // 
            // colorDialog
            // 
            this.colorDialog.Color = System.Drawing.Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(192)))), ((int)(((byte)(0)))));
            // 
            // timer_Color
            // 
            this.timer_Color.Enabled = true;
            this.timer_Color.Tick += new System.EventHandler(this.Timer_ColorTick);
            // 
            // toolStripStatusLabel_Info
            // 
            this.toolStripStatusLabel_Info.BackColor = System.Drawing.Color.Transparent;
            this.toolStripStatusLabel_Info.Font = new System.Drawing.Font("Arial Narrow", 12F, ((System.Drawing.FontStyle)((System.Drawing.FontStyle.Bold | System.Drawing.FontStyle.Italic))), System.Drawing.GraphicsUnit.Point, ((byte)(204)));
            this.toolStripStatusLabel_Info.ForeColor = System.Drawing.SystemColors.ControlDarkDark;
            this.toolStripStatusLabel_Info.Image = ((System.Drawing.Image)(resources.GetObject("toolStripStatusLabel_Info.Image")));
            this.toolStripStatusLabel_Info.Margin = new System.Windows.Forms.Padding(5);
            this.toolStripStatusLabel_Info.Name = "toolStripStatusLabel_Info";
            this.toolStripStatusLabel_Info.Size = new System.Drawing.Size(30, 30);
            // 
            // statusStrip
            // 
            this.statusStrip.ImageScalingSize = new System.Drawing.Size(30, 30);
            this.statusStrip.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.toolStripStatusLabel_Info});
            this.statusStrip.Location = new System.Drawing.Point(0, 496);
            this.statusStrip.Margin = new System.Windows.Forms.Padding(10, 0, 0, 0);
            this.statusStrip.Name = "statusStrip";
            this.statusStrip.RenderMode = System.Windows.Forms.ToolStripRenderMode.ManagerRenderMode;
            this.statusStrip.Size = new System.Drawing.Size(1834, 40);
            this.statusStrip.TabIndex = 6;
            this.statusStrip.Text = "statusStrip1";
            // 
            // ProgressBar_Info
            // 
            this.ProgressBar_Info.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.ProgressBar_Info.Location = new System.Drawing.Point(12, 491);
            this.ProgressBar_Info.Name = "ProgressBar_Info";
            this.ProgressBar_Info.Size = new System.Drawing.Size(1812, 2);
            this.ProgressBar_Info.TabIndex = 7;
            // 
            // MainForm
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
            this.ClientSize = new System.Drawing.Size(1834, 536);
            this.Controls.Add(this.ProgressBar_Info);
            this.Controls.Add(this.statusStrip);
            this.Controls.Add(this.panel1);
            this.Controls.Add(this.Grid_Order);
            this.Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
            this.Name = "MainForm";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.Text = "Журнал заявок";
            this.WindowState = System.Windows.Forms.FormWindowState.Maximized;
            this.Activated += new System.EventHandler(this.MainForm_Activated);
            this.FormClosing += new System.Windows.Forms.FormClosingEventHandler(this.MainFormFormClosing);
            this.Load += new System.EventHandler(this.MainFormLoad);
            ((System.ComponentModel.ISupportInitialize)(this.Grid_Order)).EndInit();
            this.contextMenuStrip1.ResumeLayout(false);
            this.panel1.ResumeLayout(false);
            this.panel1.PerformLayout();
            this.menuStrip1.ResumeLayout(false);
            this.menuStrip1.PerformLayout();
            this.statusStrip.ResumeLayout(false);
            this.statusStrip.PerformLayout();
            this.ResumeLayout(false);
            this.PerformLayout();

		}

		private System.Windows.Forms.Button button_ClearColor;
		private System.Windows.Forms.Button button_ColorDialog;
		private System.Windows.Forms.Button button_ColorFinish;
		private System.Windows.Forms.Button button_ColorInWork;
		private System.Windows.Forms.Button button_CopyResult;
		private System.Windows.Forms.Button button_Delete;
		private System.Windows.Forms.Button button_Edit;
		private System.Windows.Forms.Button button_NewOrder;
		private System.Windows.Forms.Button button_Result;
		private System.Windows.Forms.Button button_Sent;
		private System.Windows.Forms.Button button_SortChoiceDay;
		private System.Windows.Forms.Button button_Update;
		private System.Windows.Forms.Button button1;
		private System.Windows.Forms.CheckBox checkBox_DateDay;
		private System.Windows.Forms.CheckBox checkBox_SortChoiceDayON;
		private System.Windows.Forms.ColorDialog colorDialog;
		private System.Windows.Forms.ContextMenuStrip contextMenuStrip1;
		private System.Windows.Forms.DateTimePicker dateTimePicker_DateSort;
		private System.Windows.Forms.DataGridView Grid_Order;
		private System.Windows.Forms.MenuStrip menuStrip1;
		private System.Windows.Forms.Panel panel1;
		private System.Windows.Forms.RichTextBox richTextBox_Result;
		private System.Windows.Forms.TextBox textBox_Searh;
		private System.Windows.Forms.Timer timer_Color;
		private System.Windows.Forms.ToolStripSeparator toolStrip_Delete;
		private System.Windows.Forms.ToolStripMenuItem toolStripMenuItem1;
		private System.Windows.Forms.ToolStripSeparator toolStripSeparator1;
		private System.Windows.Forms.ToolStripMenuItem вРаботеToolStripMenuItem;
		private System.Windows.Forms.ToolStripMenuItem готовоToolStripMenuItem;
		private System.Windows.Forms.ToolStripMenuItem добавитьБЛЦБToolStripMenuItem;
		private System.Windows.Forms.ToolStripMenuItem добавитьБЛЦБToolStripMenuItem1;
		private System.Windows.Forms.ToolStripMenuItem другойЦветToolStripMenuItem;
		private System.Windows.Forms.ToolStripMenuItem редактироватьToolStripMenuItem;
		private System.Windows.Forms.ToolStripMenuItem результатToolStripMenuItem;
		private System.Windows.Forms.ToolStripMenuItem снятьВыделениеToolStripMenuItem;
		private System.Windows.Forms.ToolStripMenuItem удалитьToolStripMenuItem;
		private System.Windows.Forms.ToolStripMenuItem цветToolStripMenuItem;
		private System.Windows.Forms.ToolStripMenuItem копироватьКомментарийToolStripMenuItem;
		private System.Windows.Forms.ComboBox comboBox1;
        private System.Windows.Forms.ToolStripMenuItem telegramКомментарийToolStripMenuItem;
        private System.Windows.Forms.Button button_Clear_Searh;
        private System.Windows.Forms.ToolStripMenuItem telegramКомментарийToolStripMenuItem1;
        private System.Windows.Forms.ToolStripStatusLabel toolStripStatusLabel_Info;
        private System.Windows.Forms.StatusStrip statusStrip;
        private System.Windows.Forms.ProgressBar ProgressBar_Info;
        private System.Windows.Forms.CheckBox checkBox_Sent;
        private System.Windows.Forms.CheckBox checkBox_Finished;
        private System.Windows.Forms.CheckBox checkBox_InWork;
        private System.Windows.Forms.CheckBox checkBox_checkColor;
    }
}
	

