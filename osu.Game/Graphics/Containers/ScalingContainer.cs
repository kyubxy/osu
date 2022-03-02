// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Primitives;
using osu.Framework.Screens;
using osu.Game.Configuration;
using osu.Game.Screens;
using osu.Game.Screens.Backgrounds;
using osuTK;

namespace osu.Game.Graphics.Containers
{
    /// <summary>
    /// Handles user-defined scaling, allowing application at multiple levels defined by <see cref="ScalingMode"/>.
    /// </summary>
    public class ScalingContainer : Container
    {
        private Bindable<float> sizeX;
        private Bindable<float> sizeY;
        private Bindable<float> posX;
        private Bindable<float> posY;

        private Bindable<MarginPadding> safeAreaPadding;

        private readonly ScalingMode? targetMode;

        private Bindable<ScalingMode> scalingMode;

        private readonly Container content;
        protected override Container<Drawable> Content => content;

        public override bool ReceivePositionalInputAt(Vector2 screenSpacePos) => true;

        private readonly Container sizableContainer;

        private BackgroundScreenStack backgroundStack;

        private RectangleF? customRect;
        private bool customRectIsRelativePosition;

        /// <summary>
        /// Set a custom position and scale which overrides any user specification.
        /// </summary>
        public void SetCustomRect(RectangleF? rect, bool relativePosition = false)
        {
            customRect = rect;
            customRectIsRelativePosition = relativePosition;

            if (IsLoaded) Scheduler.AddOnce(updateSize);
        }

        private const float corner_radius = 10;

        /// <summary>
        /// Create a new instance.
        /// </summary>
        /// <param name="targetMode">The mode which this container should be handling. Handles all modes if null.</param>
        public ScalingContainer(ScalingMode? targetMode = null)
        {
            this.targetMode = targetMode;
            RelativeSizeAxes = Axes.Both;

            InternalChild = sizableContainer = new AlwaysInputContainer
            {
                RelativeSizeAxes = Axes.Both,
                RelativePositionAxes = Axes.Both,
                CornerRadius = corner_radius,
                Child = content = new ScalingDrawSizePreservingFillContainer(targetMode != ScalingMode.Gameplay)
            };
        }

        private class ScalingDrawSizePreservingFillContainer : DrawSizePreservingFillContainer
        {
            private readonly bool applyUIScale;
            private Bindable<float> uiScale;

            public override bool ReceivePositionalInputAt(Vector2 screenSpacePos) => true;

            public ScalingDrawSizePreservingFillContainer(bool applyUIScale)
            {
                this.applyUIScale = applyUIScale;
            }

            [BackgroundDependencyLoader]
            private void load(OsuConfigManager osuConfig)
            {
                if (applyUIScale)
                {
                    uiScale = osuConfig.GetBindable<float>(OsuSetting.UIScale);
                    uiScale.BindValueChanged(scaleChanged, true);
                }
            }

            private void scaleChanged(ValueChangedEvent<float> args)
            {
                this.ScaleTo(new Vector2(args.NewValue), 500, Easing.Out);
                this.ResizeTo(new Vector2(1 / args.NewValue), 500, Easing.Out);
            }
        }

        [BackgroundDependencyLoader]
        private void load(OsuConfigManager config, ISafeArea safeArea)
        {
            scalingMode = config.GetBindable<ScalingMode>(OsuSetting.Scaling);
            scalingMode.ValueChanged += _ => Scheduler.AddOnce(updateSize);

            sizeX = config.GetBindable<float>(OsuSetting.ScalingSizeX);
            sizeX.ValueChanged += _ => Scheduler.AddOnce(updateSize);

            sizeY = config.GetBindable<float>(OsuSetting.ScalingSizeY);
            sizeY.ValueChanged += _ => Scheduler.AddOnce(updateSize);

            posX = config.GetBindable<float>(OsuSetting.ScalingPositionX);
            posX.ValueChanged += _ => Scheduler.AddOnce(updateSize);

            posY = config.GetBindable<float>(OsuSetting.ScalingPositionY);
            posY.ValueChanged += _ => Scheduler.AddOnce(updateSize);

            safeAreaPadding = safeArea.SafeAreaPadding.GetBoundCopy();
            safeAreaPadding.BindValueChanged(_ => Scheduler.AddOnce(updateSize));
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            updateSize();
            sizableContainer.FinishTransforms();
        }

        private bool requiresBackgroundVisible => (scalingMode.Value == ScalingMode.Everything || scalingMode.Value == ScalingMode.ExcludeOverlays) && (sizeX.Value != 1 || sizeY.Value != 1);

        private void updateSize()
        {
            const float duration = 500;

            if (targetMode == ScalingMode.Everything)
            {
                // the top level scaling container manages the background to be displayed while scaling.
                if (requiresBackgroundVisible)
                {
                    if (backgroundStack == null)
                    {
                        AddInternal(backgroundStack = new BackgroundScreenStack
                        {
                            Colour = OsuColour.Gray(0.1f),
                            Alpha = 0,
                            Depth = float.MaxValue
                        });

                        backgroundStack.Push(new ScalingBackgroundScreen());
                    }

                    backgroundStack.FadeIn(duration);
                }
                else
                    backgroundStack?.FadeOut(duration);
            }

            RectangleF targetRect = new RectangleF(Vector2.Zero, Vector2.One);

            if (customRect != null)
            {
                sizableContainer.RelativePositionAxes = customRectIsRelativePosition ? Axes.Both : Axes.None;

                targetRect = customRect.Value;
            }
            else if (targetMode == null || scalingMode.Value == targetMode)
            {
                sizableContainer.RelativePositionAxes = Axes.Both;

                Vector2 scale = new Vector2(sizeX.Value, sizeY.Value);
                Vector2 pos = new Vector2(posX.Value, posY.Value) * (Vector2.One - scale);

                targetRect = new RectangleF(pos, scale);
            }

            bool requiresMasking = targetRect.Size != Vector2.One
                                   // For the top level scaling container, for now we apply masking if safe areas are in use.
                                   // In the future this can likely be removed as more of the actual UI supports overflowing into the safe areas.
                                   || (targetMode == ScalingMode.Everything && safeAreaPadding.Value.Total != Vector2.Zero);

            if (requiresMasking)
                sizableContainer.Masking = true;

            sizableContainer.MoveTo(targetRect.Location, duration, Easing.OutQuart);
            sizableContainer.ResizeTo(targetRect.Size, duration, Easing.OutQuart);

            // Of note, this will not working great in the case of nested ScalingContainers where multiple are applying corner radius.
            // There should likely only be masking and corner radius applied at one point in the full game stack to fix this.
            // An example of how this can occur is it the skin editor is visible and the game screen scaling is set to "Everything".
            sizableContainer.TransformTo(nameof(CornerRadius), requiresMasking ? corner_radius : 0, duration, requiresMasking ? Easing.OutQuart : Easing.None)
                            .OnComplete(_ => { sizableContainer.Masking = requiresMasking; });
        }

        private class ScalingBackgroundScreen : BackgroundScreenDefault
        {
            protected override bool AllowStoryboardBackground => false;

            public override void OnEntering(IScreen last)
            {
                this.FadeInFromZero(4000, Easing.OutQuint);
            }
        }

        private class AlwaysInputContainer : Container
        {
            public override bool ReceivePositionalInputAt(Vector2 screenSpacePos) => true;

            public AlwaysInputContainer()
            {
                RelativeSizeAxes = Axes.Both;
            }
        }
    }
}
