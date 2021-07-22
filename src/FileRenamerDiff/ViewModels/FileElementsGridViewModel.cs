﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.ComponentModel;
using System.Resources;
using System.Globalization;
using System.Windows.Data;
using System.Collections;
using System.Collections.ObjectModel;
using System.Threading.Tasks;

using Livet;
using Livet.Commands;
using Livet.Messaging;
using Livet.Messaging.IO;
using Livet.EventListeners;
using Livet.Messaging.Windows;

using Reactive.Bindings;
using System.Reactive;
using System.Reactive.Linq;
using Reactive.Bindings.Extensions;
using Anotar.Serilog;

using FileRenamerDiff.Models;
using FileRenamerDiff.Properties;

namespace FileRenamerDiff.ViewModels
{
    /// <summary>
    /// ファイル情報VMコレクションを含んだDataGrid用VM
    /// </summary>
    public class FileElementsGridViewModel : ViewModel
    {
        /// <summary>
        /// ファイル情報コレクションのDataGrid用のICollectionView
        /// </summary>
        public ICollectionView CViewFileElementVMs { get; }

        /// <summary>
        /// リネーム前後での変更があったファイル数
        /// </summary>
        public IReadOnlyReactiveProperty<int> CountReplaced { get; }
        /// <summary>
        /// リネーム前後で変更が１つでのあったか
        /// </summary>
        public IReadOnlyReactiveProperty<bool> IsReplacedAny { get; }

        /// <summary>
        /// 置換前後で差があったファイルのみ表示するか
        /// </summary>
        public ReactivePropertySlim<bool> IsVisibleReplacedOnly { get; } = new(false);

        /// <summary>
        /// ファイルパスの衝突しているファイル数
        /// </summary>
        public IReadOnlyReactiveProperty<int> CountConflicted { get; }

        /// <summary>
        /// ファイルパスの衝突がないか
        /// </summary>
        public IReadOnlyReactiveProperty<bool> IsNotConflictedAny { get; }

        /// <summary>
        /// ファイルパスが衝突しているファイルのみ表示するか
        /// </summary>
        public ReactivePropertySlim<bool> IsVisibleConflictedOnly { get; } = new(false);

        /// <summary>
        /// ファイルが1つでもあるか
        /// </summary>
        public ReadOnlyReactivePropertySlim<bool> IsAnyFiles { get; }

        /// <summary>
        /// 直接ファイル追加
        /// </summary>
        public ReactiveCommand<IReadOnlyList<string>> AddTargetFilesCommand { get; }
        /// <summary>
        /// ファイルリストの全消去
        /// </summary>
        public ReactiveCommand ClearFileElementsCommand { get; }
        /// <summary>
        /// ファイルからの削除
        /// </summary>
        public ReactiveCommand<FileElementViewModel> RemoveItemCommand { get; } = new();

        /// <summary>
        /// デザイナー用です　コードからは呼べません
        /// </summary>
        [Obsolete("Designer only", true)]
        public FileElementsGridViewModel() : this(DesignerModel.MainModelForDesigner) { }

        public FileElementsGridViewModel(MainModel mainModel)
        {
            this.CountReplaced = mainModel.CountReplaced.ObserveOnUIDispatcher().ToReadOnlyReactivePropertySlim();
            this.IsReplacedAny = CountReplaced.Select(x => x > 0).ToReadOnlyReactivePropertySlim();
            this.CountConflicted = mainModel.CountConflicted.ObserveOnUIDispatcher().ToReadOnlyReactivePropertySlim();
            this.IsNotConflictedAny = CountConflicted.Select(x => x <= 0).ToReadOnlyReactivePropertySlim();

            var fileElementVMs = mainModel.FileElementModels
                .ToReadOnlyReactiveCollection(x => new FileElementViewModel(x), ReactivePropertyScheduler.Default);

            this.CViewFileElementVMs = CreateCollectionViewFilePathVMs(fileElementVMs);

            //表示基準に変更があったら、表示判定対象に変更があったら、CollectionViewの表示を更新する
            new[]
            {
                this.IsVisibleReplacedOnly,
                this.IsVisibleConflictedOnly,
                this.CountConflicted.Select(_=>true),
                this.CountReplaced.Select(_=>true),
            }
            .CombineLatest()
            .Throttle(TimeSpan.FromMilliseconds(100))
            .ObserveOnUIDispatcher()
            .Subscribe(_ => RefleshCollectionViewSafe());

            this.IsReplacedAny
                .Where(x => x == false)
                .Subscribe(_ =>
                    this.IsVisibleReplacedOnly.Value = false);

            AddTargetFilesCommand = mainModel.IsIdleUI
                .ToReactiveCommand<IReadOnlyList<string>>()
                .WithSubscribe(x => mainModel.AddTargetFiles(x));

            this.IsAnyFiles = mainModel.FileElementModels.ObserveIsAny().ToReadOnlyReactivePropertySlim();

            this.ClearFileElementsCommand =
                (new[]
                {
                    mainModel.IsIdle,
                    IsAnyFiles,
                })
                .CombineLatestValuesAreAllTrue()
                .ObserveOnUIDispatcher()
                .ToReactiveCommand()
                .WithSubscribe(() => mainModel.FileElementModels.Clear());


            RemoveItemCommand = mainModel.IsIdleUI
                .ToReactiveCommand<FileElementViewModel>()
                .WithSubscribe(x =>
                    mainModel.FileElementModels.Remove(x.PathModel));
        }

        private ICollectionView CreateCollectionViewFilePathVMs(object fVMs)
        {
            ICollectionView cView = CollectionViewSource.GetDefaultView(fVMs);
            cView.Filter = (x => GetVisibleRow(x));
            return cView;
        }

        /// <summary>
        /// 2つの表示切り替えプロパティと、各行の値に応じて、その行の表示状態を決定する
        /// </summary>
        /// <param name="row">行VM</param>
        /// <returns>表示状態</returns>
        private bool GetVisibleRow(object row)
        {
            if (row is not FileElementViewModel pathVM)
                return true;

            bool replacedVisible = !IsVisibleReplacedOnly.Value || pathVM.IsReplaced.Value;
            bool conflictedVisible = !IsVisibleConflictedOnly.Value || pathVM.IsConflicted.Value;

            return replacedVisible && conflictedVisible;
        }

        private void RefleshCollectionViewSafe()
        {
            if (CViewFileElementVMs is not ListCollectionView currentView)
                return;

            //なぜかCollectionViewが追加中・編集中のことがある。
            if (currentView.IsAddingNew)
            {
                LogTo.Warning("CollectionView is Adding");
                currentView.CancelNew();
            }
            if (currentView.IsEditingItem)
            {
                LogTo.Warning("CollectionView is Editing");
                currentView.CommitEdit();
            }
            currentView.Refresh();
        }
    }
}