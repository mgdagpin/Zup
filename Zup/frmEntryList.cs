﻿using Microsoft.EntityFrameworkCore;

using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Runtime.InteropServices;

using Zup.CustomControls;
using Zup.Entities;
using Zup.EventArguments;
using TaskStatus = Zup.CustomControls.TaskStatus;

namespace Zup;

public partial class frmEntryList : Form
{
    const int maxOngoingTaskRow = 4;
    const int maxQueuedTaskRow = 2;
    const int maxRankedTaskRow = 4;

    private frmNewEntry m_FormNewEntry;
    private frmUpdateEntry m_FormUpdateEntry; // need to make this as singleton to minimize loading
    private frmMain m_FormMain = null!;

    private bool p_OnLoad = true;
    private readonly ZupDbContext m_DbContext;

    private Guid? CurrentRunningTaskID;
    private Guid? LastRunningTaskID;

    public bool ListIsReady { get; set; }

    public event EventHandler<ListReadyEventArgs>? OnListReadyEvent;
    public event EventHandler<QueueTaskUpdatedEventArgs>? OnQueueTaskUpdatedEvent;
    public event EventHandler<TokenEventArgs>? OnTokenDoubleClicked;

    #region Draggable Form
    public const int WM_NCLBUTTONDOWN = 0xA1;
    public const int HT_CAPTION = 0x2;

    [DllImport("user32.dll")]
    public static extern int SendMessage(IntPtr hWnd, int Msg, int wParam, int lParam);

    [DllImport("user32.dll")]
    public static extern bool ReleaseCapture();

    private void frmEntryList_MouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            ReleaseCapture();
            SendMessage(Handle, WM_NCLBUTTONDOWN, HT_CAPTION, 0);
        }
    }

    private void frmEntryList_Move(object sender, EventArgs e)
    {
        tmrSaveSetting.Enabled = false;
        tmrSaveSetting.Enabled = true;
    }

    private void tmrSaveSetting_Tick(object sender, EventArgs e)
    {
        Properties.Settings.Default.FormLocationX = Left;
        Properties.Settings.Default.FormLocationY = Top;
        Properties.Settings.Default.Save();

        tmrSaveSetting.Enabled = false;
    }

    public void MoveToCenter()
    {
        Left = (Screen.PrimaryScreen!.WorkingArea.Width / 2) - (Width / 2);
        Top = (Screen.PrimaryScreen!.WorkingArea.Height / 2) - (Height / 2);
    }

    public void SetFormMain(frmMain frmMain)
    {
        m_FormMain = frmMain;
        m_FormUpdateEntry.SetFormMain(frmMain);
    }

    private void UpdateFormPosition()
    {
        if (Properties.Settings.Default.FormLocationX == 0
            && Properties.Settings.Default.FormLocationY == 0)
        {
            Left = Screen.PrimaryScreen!.WorkingArea.Width - Width - 2;
            Top = Screen.PrimaryScreen!.WorkingArea.Height - Height - 2;
        }
        else
        {
            Left = Properties.Settings.Default.FormLocationX;
            Top = Properties.Settings.Default.FormLocationY;
        }
    }
    #endregion

    protected override CreateParams CreateParams
    {
        get
        {
            var Params = base.CreateParams;
            Params.ExStyle |= 0x00000080;
            return Params;
        }
    }

    public frmEntryList(ZupDbContext dbContext, frmNewEntry frmNewEntry, frmUpdateEntry frmUpdateEntry)
    {
        InitializeComponent();

        m_DbContext = dbContext;
        m_FormNewEntry = frmNewEntry;
        m_FormUpdateEntry = frmUpdateEntry;

        m_DbContext.Database.Migrate();
    }

    private void frmEntryList_Load(object sender, EventArgs e)
    {
        m_FormNewEntry.OnNewEntryEvent += EachEntry_NewEntryEventHandler;
        m_FormUpdateEntry.OnDeleteEvent += FormUpdateEntry_OnDeleteEventHandler;
        m_FormUpdateEntry.OnSavedEvent += FormUpdateEntry_OnSavedEventHandler;
        m_FormUpdateEntry.OnTokenDoubleClicked += FormUpdateEntry_OnTokenDoubleClicked;
        m_FormUpdateEntry.OnReRunEvent += FormUpdateEntry_OnRerunEventHandler;

        var list = LoadListToControl();

        ListIsReady = true;

        if (OnListReadyEvent != null)
        {
            OnListReadyEvent(this, new ListReadyEventArgs(list.HasItems));
        }

        if (OnQueueTaskUpdatedEvent != null)
        {
            OnQueueTaskUpdatedEvent(this, new QueueTaskUpdatedEventArgs(list.QueuedTasksCount));
        }

        p_OnLoad = false;

        Opacity = Properties.Settings.Default.EntryListOpacity;

        RefreshList();
        UpdateFormPosition();

        showQueuedTasksToolStripMenuItem.Checked = Properties.Settings.Default.ShowQueuedTasks;
        showRankedTasksToolStripMenuItem.Checked = Properties.Settings.Default.ShowRankedTasks;
        showClosedTasksToolStripMenuItem.Checked = Properties.Settings.Default.ShowClosedTasks;
    }

    private void RefreshList()
    {
        UpdateTablePanel();
        SetListScrollAndActiveControl();
        ConfigureFlowLayoutPanel();
        ResizeForm();
    }

    private void ConfigureFlowLayoutPanel()
    {
        flpTaskList.AutoScroll = flpTaskList.Controls.Count > 1;
        flpQueuedTaskList.AutoScroll = flpQueuedTaskList.Controls.Count > 1;
        flpRankedTasks.AutoScroll = flpRankedTasks.Controls.Count > 1;
    }

    private void UpdateTablePanel()
    {
        int rowIx = 0;

        tblLayoutPanel.Controls.Clear();
        tblLayoutPanel.RowStyles.Clear();

        tblLayoutPanel.Controls.Add(flpTaskList, 0, rowIx);

        if (flpQueuedTaskList.Controls.Count > 0)
        {
            tblLayoutPanel.Controls.Add(flpQueuedTaskList, 0, ++rowIx);
        }

        if (flpRankedTasks.Controls.Count > 0)
        {
            tblLayoutPanel.Controls.Add(flpRankedTasks, 0, ++rowIx);
        }

        tblLayoutPanel.RowCount = rowIx + 1;

        var oRowHeight = CommonUtility.GetMin((int)EachEntry.OngoingRowHeight * flpTaskList.Controls.Count, (int)EachEntry.OngoingRowHeight * maxOngoingTaskRow);
        tblLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, oRowHeight));

        if (flpQueuedTaskList.Controls.Count > 0 && flpRankedTasks.Controls.Count > 0)
        {
            if (flpQueuedTaskList.Controls.Count > 0)
            {
                var qRowHeight = CommonUtility.GetMin((int)EachEntry.RowHeight * flpQueuedTaskList.Controls.Count, (int)EachEntry.RowHeight * maxQueuedTaskRow);

                tblLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, qRowHeight));
            }

            if (flpRankedTasks.Controls.Count > 0)
            {
                var rRowHeight = CommonUtility.GetMin((int)EachEntry.RowHeight * flpRankedTasks.Controls.Count, (int)EachEntry.RowHeight * maxRankedTaskRow);

                tblLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, rRowHeight));
            }
        }
        else if (flpQueuedTaskList.Controls.Count > 0 || flpRankedTasks.Controls.Count > 0)
        {
            if (flpQueuedTaskList.Controls.Count > 0)
            {
                var qRowHeight = CommonUtility.GetMin((int)EachEntry.RowHeight * flpQueuedTaskList.Controls.Count, (int)EachEntry.RowHeight * maxQueuedTaskRow);

                tblLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, qRowHeight));
            }

            if (flpRankedTasks.Controls.Count > 0)
            {
                var rRowHeight = CommonUtility.GetMin((int)EachEntry.RowHeight * flpRankedTasks.Controls.Count, (int)EachEntry.RowHeight * maxRankedTaskRow);

                tblLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, rRowHeight));
            }
        }
    }

    private void SetListScrollAndActiveControl()
    {
        var firstItem = flpTaskList.Controls.Count > 0
            ? (EachEntry)flpTaskList.Controls[flpTaskList.Controls.Count - 1]
            : null;


        if (firstItem != null)
        {
            flpTaskList.ScrollControlIntoView(firstItem);

            firstItem.IsFirstItem = true;
        }

        if (flpTaskList.Controls.Count > 0)
        {
            ActiveControl = flpTaskList.Controls[0];
        }
    }

    private void frmEntryList_FormClosing(object sender, FormClosingEventArgs e)
    {
        e.Cancel = true;

        Hide();
    }

    public void ShowNewEntry()
    {
        var suggestions = new List<string>();

        var currentList = flpTaskList.Controls.Cast<EachEntry>()
            .Where(a => a.Visible && a.TaskStatus == TaskStatus.Closed)
            .Select(a => a.Text)
            .ToArray();

        if (currentList.Length > 1)
        {
            suggestions.Add(currentList[1]);
        }

        foreach (var item in currentList)
        {
            if (suggestions.Contains(item))
            {
                continue;
            }

            suggestions.Add(item);
        }

        m_FormNewEntry.ShowNewEntryDialog(suggestions.ToArray());
    }



    private void FormUpdateEntry_OnTokenDoubleClicked(object? sender, TokenEventArgs e)
    {
        OnTokenDoubleClicked?.Invoke(sender, e);
    }

    private LoadedListControlDetail LoadListToControl()
    {
        var result = new LoadedListControlDetail();

        var minDate = DateTime.Now.AddDays(-Properties.Settings.Default.NumDaysOfDataToLoad);

        var ongoingTasks = new List<EachEntry>();
        var queuedTasks = new List<EachEntry>();
        var rankedTasks = new List<EachEntry>();

        foreach (var task in m_DbContext.TaskEntries.Where(a => a.CreatedOn >= minDate || a.StartedOn == null).ToList())
        {
            var eachEntry = new EachEntry(task.ID, task.Task, task.CreatedOn, task.StartedOn, task.EndedOn, task.Reminder)
            {
                Rank = task.Rank,
                TabStop = false,

            };

            eachEntry.GotFocus += (sender, e) => ActiveControl = null;

            eachEntry.OnResumeEvent += EachEntry_OnResumeEventHandler;
            eachEntry.OnStopEvent += EachEntry_OnStopEventHandler;
            eachEntry.OnStartEvent += EachEntry_OnStartEvent;
            eachEntry.OnUpdateEvent += EachEntry_OnUpdateEvent;
            eachEntry.OnStartQueueEvent += EachEntry_OnStartQueueEventHandler;
            eachEntry.TaskMouseDown += new MouseEventHandler(frmEntryList_MouseDown);
            eachEntry.TaskRightClick += EachEntry_TaskRightClick;

            if (eachEntry.TaskStatus == TaskStatus.Ranked)
            {
                if (!Properties.Settings.Default.ShowRankedTasks)
                {
                    continue;
                }

                rankedTasks.Add(eachEntry);
                result.RankedTasksCount++;
                continue;
            }

            if (eachEntry.TaskStatus == TaskStatus.Queued)
            {
                if (!Properties.Settings.Default.ShowQueuedTasks)
                {
                    continue;
                }

                queuedTasks.Add(eachEntry);
                result.QueuedTasksCount++;
                continue;
            }

            if (eachEntry.TaskStatus == TaskStatus.Closed && !Properties.Settings.Default.ShowClosedTasks)
            {
                continue;
            }

            ongoingTasks.Add(eachEntry);

            result.OngoingTasksCount++;
        }

        SortTasks(ongoingTasks);

        flpTaskList.SuspendLayout();
        flpTaskList.Controls.Clear();
        flpTaskList.Controls.AddRange(ongoingTasks.ToArray());
        flpTaskList.ResumeLayout();

        flpQueuedTaskList.SuspendLayout();
        flpQueuedTaskList.Controls.Clear();
        flpQueuedTaskList.Controls.AddRange(queuedTasks.OrderBy(a => a.CreatedOn).ToArray());
        flpQueuedTaskList.ResumeLayout();

        flpRankedTasks.SuspendLayout();
        flpRankedTasks.Controls.Clear();
        flpRankedTasks.Controls.AddRange(rankedTasks.OrderBy(a => a.Rank).ToArray());
        flpRankedTasks.ResumeLayout();

        return result;
    }    

    public void SortTasks(IList entryList)
    {
        var stack = new Queue<EachEntry>();
        var list = entryList.OfType<EachEntry>().Where(a => a.Visible).ToList();
        var all = entryList.OfType<EachEntry>().Where(a => a.Visible).ToArray();
        var minDate = DateTime.Now.AddDays(-Properties.Settings.Default.NumDaysOfDataToLoad);

        // running
        foreach (var item in all.Where(a => a.TaskStatus == TaskStatus.Running).OrderBy(a => a.CreatedOn))
        {
            if (!list.Contains(item))
            {
                continue;
            }

            stack.Enqueue(item);
            list.Remove(item);
        }

        // started but not yet closed
        foreach (var item in all.Where(a => a.TaskStatus == TaskStatus.Unclosed))
        {
            if (!list.Contains(item))
            {
                continue;
            }

            stack.Enqueue(item);
            list.Remove(item);
        }

        // with ranking
        foreach (var item in all.Where(a => a.TaskStatus == TaskStatus.Ranked).OrderBy(a => a.Rank))
        {
            if (!list.Contains(item))
            {
                continue;
            }

            stack.Enqueue(item);
            list.Remove(item);
        }

        // not yet started
        foreach (var item in all.Where(a => a.TaskStatus == TaskStatus.Queued).OrderByDescending(a => a.CreatedOn))
        {
            if (!list.Contains(item))
            {
                continue;
            }

            stack.Enqueue(item);
            list.Remove(item);
        }

        // closed items
        foreach (var item in all.Where(a => a.TaskStatus == TaskStatus.Closed).OrderByDescending(a => a.StartedOn))
        {
            if (!list.Contains(item))
            {
                continue;
            }

            stack.Enqueue(item);
            list.Remove(item);
        }

        foreach (var item in list)
        {
            stack.Enqueue(item);
        }

        var i = 0;
        while (stack.TryDequeue(out var entry))
        {
            // Sorting also called when resuming tasks so the passed parameter entryList is a ControlCollection
            // otherwise it is a list generated upon initialize or refresh
            if (entryList is Control.ControlCollection controls)
            {
                controls.SetChildIndex(entry, i);
            }
            else
            {
                entryList.Remove(entry);
                entryList.Insert(i, entry);
            }

            i++;
        }
    }

    public void ResizeForm()
    {
        var itemCount = CommonUtility.GetMin(flpTaskList.Controls.Count, maxOngoingTaskRow);

        itemCount += CommonUtility.GetMin(flpQueuedTaskList.Controls.Count, maxQueuedTaskRow);
        itemCount += CommonUtility.GetMin(flpRankedTasks.Controls.Count, maxRankedTaskRow);

        // itemCount = Properties.Settings.Default.ItemsToShow;

        int totalHeight = (int)EachEntry.OngoingRowHeight * CommonUtility.GetMin(flpTaskList.Controls.Count, maxOngoingTaskRow);

        totalHeight += (int)EachEntry.RowHeight * CommonUtility.GetMin(flpQueuedTaskList.Controls.Count, maxQueuedTaskRow);
        totalHeight += (int)EachEntry.RowHeight * CommonUtility.GetMin(flpRankedTasks.Controls.Count, maxRankedTaskRow);

        // margin-bottom
        // totalHeight += 1 * itemCount;

        Height = totalHeight;
    }

    private void FormUpdateEntry_OnSavedEventHandler(object? sender, SaveEventArgs args)
    {
        var eachEntry = flpTaskList.Controls.Cast<EachEntry>().SingleOrDefault(a => a.EntryID == args.Task.ID)
            ?? flpQueuedTaskList.Controls.Cast<EachEntry>().SingleOrDefault(a => a.EntryID == args.Task.ID)
            ?? flpRankedTasks.Controls.Cast<EachEntry>().SingleOrDefault(a => a.EntryID == args.Task.ID);

        if (eachEntry != null)
        {
            eachEntry.Text = args.Task.Task;
            eachEntry.StartedOn = args.Task.StartedOn;
            eachEntry.EndedOn = args.Task.EndedOn;
            eachEntry.Rank = args.Task.Rank;
        }
    }

    private void EachEntry_OnStartEvent(object? sender, OnStartEventArgs args)
    {
        var eachEntry = (EachEntry)sender!;

        CurrentRunningTaskID = eachEntry.EntryID;
        LastRunningTaskID = eachEntry.EntryID;
    }

    private void FormUpdateEntry_OnDeleteEventHandler(Guid entryID)
    {
        DeleteTimeLog(entryID);

        ResizeForm();

        if (OnQueueTaskUpdatedEvent != null)
        {
            OnQueueTaskUpdatedEvent(this, new QueueTaskUpdatedEventArgs(GetQueueCount(false)));
        }
    }

    private void DeleteTimeLog(Guid entryID)
    {
        var entry = m_DbContext.TaskEntries.Find(entryID);

        if (entry != null)
        {
            m_DbContext.TaskEntries.Remove(entry);
            m_DbContext.SaveChanges();
        }

        var entryToRemove = flpTaskList.Controls.Cast<EachEntry>().SingleOrDefault(a => a.EntryID == entryID);

        if (entryToRemove != null)
        {
            // only hide, remove will auto scroll the list to bottom because the list is in reverse
            entryToRemove.Hide();
        }
    }

    private void FormUpdateEntry_OnRerunEventHandler(object? sender, NewEntryEventArgs args)
    {
        EachEntry_NewEntryEventHandler(sender, args);
    }

    private void EachEntry_OnResumeEventHandler(object? sender, NewEntryEventArgs args)
    {
        EachEntry_NewEntryEventHandler(sender, args);
    }

    private void EachEntry_OnStartQueueEventHandler(object? sender, NewEntryEventArgs args)
    {
        var eachEntryStatus = ((EachEntry)sender!).TaskStatus;

        if (eachEntryStatus == TaskStatus.Queued)
        {
            var queuedEntry = flpQueuedTaskList.Controls.Cast<EachEntry>().Single(a => a.EntryID == ((EachEntry)sender!).EntryID);

            flpQueuedTaskList.Controls.Remove(queuedEntry);
        }

        EachEntry_NewEntryEventHandler(sender, args);
    }

    private void EachEntry_NewEntryEventHandler(object? sender, NewEntryEventArgs args)
    {
        var newE = new tbl_TaskEntry
        {
            ID = Guid.NewGuid(),
            Task = args.Entry,
            CreatedOn = DateTime.Now
        };

        if (args.StartNow)
        {
            newE.StartedOn = DateTime.Now;
        }

        m_DbContext.TaskEntries.Add(newE);

        var parentEntry = args.ParentEntryID != null
            ? m_DbContext.TaskEntries.Find(args.ParentEntryID)
            : null;

        // bring notes, tags and rank from parent, this is when the user started a queued task
        if (parentEntry != null)
        {
            if (args.BringNotes)
            {
                foreach (var note in m_DbContext.TaskEntryNotes.Where(a => a.TaskID == parentEntry.ID).ToList())
                {
                    m_DbContext.TaskEntryNotes.Add(new tbl_TaskEntryNote
                    {
                        ID = Guid.NewGuid(),
                        TaskID = newE.ID,
                        CreatedOn = note.CreatedOn,
                        Notes = note.Notes,
                        RTF = note.RTF,
                        UpdatedOn = note.UpdatedOn
                    });
                }
            }

            if (args.BringTags)
            {
                foreach (var tag in m_DbContext.TaskEntryTags.Where(a => a.TaskID == parentEntry.ID).ToList())
                {
                    m_DbContext.TaskEntryTags.Add(new tbl_TaskEntryTag
                    {
                        CreatedOn = tag.CreatedOn,
                        TaskID = newE.ID,
                        TagID = tag.TagID
                    });
                }
            }
        }

        if (args.GetTags && parentEntry == null)
        {
            var minDate = DateTime.Now.AddDays(-Properties.Settings.Default.NumDaysOfDataToLoad);

            var tagIDs = (from e in m_DbContext.TaskEntries.Where(a => (a.StartedOn >= minDate && a.EndedOn != null) || a.StartedOn == null || (a.StartedOn != null && a.EndedOn == null))
                          join t in m_DbContext.TaskEntryTags on e.ID equals t.TaskID
                          orderby t.CreatedOn descending
                          where e.Task == args.Entry
                          select t.TagID)
                             .Distinct();

            foreach (var tagID in tagIDs)
            {
                m_DbContext.TaskEntryTags.Add(new tbl_TaskEntryTag
                {
                    CreatedOn = DateTime.Now,
                    TaskID = newE.ID,
                    TagID = tagID
                });
            }
        }

        m_DbContext.SaveChanges();

        var eachEntry = new EachEntry(newE.ID, newE.Task, newE.CreatedOn, newE.StartedOn, null);

        //if (newE.Rank != null)
        //{
        //    eachEntry.Rank = newE.Rank;
        //}

        eachEntry.GotFocus += (sender, e) => ActiveControl = null;

        eachEntry.OnResumeEvent += EachEntry_OnResumeEventHandler;
        eachEntry.OnStopEvent += EachEntry_OnStopEventHandler;
        eachEntry.OnStartEvent += EachEntry_OnStartEvent;
        eachEntry.OnUpdateEvent += EachEntry_OnUpdateEvent;
        eachEntry.OnStartQueueEvent += EachEntry_OnStartQueueEventHandler;
        eachEntry.TaskMouseDown += new MouseEventHandler(frmEntryList_MouseDown);
        eachEntry.TaskRightClick += EachEntry_TaskRightClick;

        //if (eachEntry.TaskStatus == TaskStatus.Ranked)
        //{
        //    flpRankedTasks.Controls.Add(eachEntry);
        //}
        //else 
        if (eachEntry.TaskStatus == TaskStatus.Queued)
        {
            flpQueuedTaskList.Controls.Add(eachEntry);
        }
        else
        {
            AddEntryToFlowLayoutControl(eachEntry, args);
        }

        SortTasks(flpTaskList.Controls);

        if (args.HideParent && args.ParentEntryID != null)
        {
            DeleteTimeLog(args.ParentEntryID.Value);
        }

        if (Properties.Settings.Default.AutoOpenUpdateWindow)
        {
            ShowUpdateEntry(newE.ID);
        }

        RefreshList();

        if (OnQueueTaskUpdatedEvent != null)
        {
            OnQueueTaskUpdatedEvent(this, new QueueTaskUpdatedEventArgs(GetQueueCount(false)));
        }
    }

    private int GetQueueCount(bool includeHidden)
    {
        return flpTaskList.Controls.Cast<EachEntry>().Count(a => a.StartedOn == null && (a.Visible || includeHidden));
    }

    private void EachEntry_OnUpdateEvent(Guid id)
    {
        ShowUpdateEntry(id);
    }

    public async void ShowUpdateEntry(Guid entryID, bool canReRun = false)
    {
        var eachEntry = flpTaskList.Controls.Cast<EachEntry>().SingleOrDefault(a => a.EntryID == entryID)
            ?? flpQueuedTaskList.Controls.Cast<EachEntry>().SingleOrDefault(a => a.EntryID == entryID)
            ?? flpRankedTasks.Controls.Cast<EachEntry>().SingleOrDefault(a => a.EntryID == entryID);

        if (eachEntry != null)
        {
            await m_FormUpdateEntry.ShowUpdateEntry(eachEntry);
        }
    }

    private void AddEntryToFlowLayoutControl(EachEntry newEntry, NewEntryEventArgs args)
    {
        flpTaskList.Controls.Add(newEntry);

        if (!p_OnLoad)
        {
            foreach (EachEntry item in flpTaskList.Controls)
            {
                item.IsFirstItem = false;

                if (args.StopOtherTask)
                {
                    if (item.IsStarted)
                    {
                        item.Stop();
                    }

                    if (item.StartedOn == null)
                    {
                        item.IsExpanded = false;
                    }
                }
            }

            if (newEntry.StartedOn != null)
            {
                newEntry.Start();
            }

            newEntry.IsFirstItem = true;
        }
    }

    private void EachEntry_OnStopEventHandler(Guid id, DateTime endOn)
    {
        var existingE = m_DbContext.TaskEntries.Find(id);

        if (existingE != null)
        {
            existingE.EndedOn = endOn;

            m_DbContext.SaveChanges();
        }

        CurrentRunningTaskID = null;
    }

    public void UpdateCurrentRunningTask()
    {
        if (CurrentRunningTaskID == null)
        {
            return;
        }

        ShowUpdateEntry(CurrentRunningTaskID.Value);        
    }

    public void ToggleLastRunningTask()
    {
        if (LastRunningTaskID == null)
        {
            return;
        }

        foreach (EachEntry item in flpTaskList.Controls)
        {
            if (item.EntryID != LastRunningTaskID)
            {
                continue;
            }

            if (item.IsStarted)
            {
                item.Stop();
            }
            else
            {
                item.Start();
            }
        }
    }

    private void showQueuedTasksToolStripMenuItem_Click(object sender, EventArgs e)
    {
        Properties.Settings.Default.ShowQueuedTasks = !Properties.Settings.Default.ShowQueuedTasks;
        Properties.Settings.Default.Save();

        showQueuedTasksToolStripMenuItem.Checked = Properties.Settings.Default.ShowQueuedTasks;

        LoadListToControl();

        RefreshList();
    }

    private void showRankedTasksToolStripMenuItem_Click(object sender, EventArgs e)
    {
        Properties.Settings.Default.ShowRankedTasks = !Properties.Settings.Default.ShowRankedTasks;
        Properties.Settings.Default.Save();

        showRankedTasksToolStripMenuItem.Checked = Properties.Settings.Default.ShowRankedTasks;

        LoadListToControl();

        RefreshList();
    }

    private void showClosedTasksToolStripMenuItem_Click(object sender, EventArgs e)
    {
        Properties.Settings.Default.ShowClosedTasks = !Properties.Settings.Default.ShowClosedTasks;
        Properties.Settings.Default.Save();

        showClosedTasksToolStripMenuItem.Checked = Properties.Settings.Default.ShowClosedTasks;

        LoadListToControl();

        RefreshList();
    }

    private void reorderToolStripMenuItem_Click(object sender, EventArgs e)
    {
        LoadListToControl();

        RefreshList();
    }

    private void deleteEntryToolStripMenuItem_Click(object sender, EventArgs e)
    {

    }

    private void EachEntry_TaskRightClick(object? sender, MouseEventArgs e)
    {

    }
}