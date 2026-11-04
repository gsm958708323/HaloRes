namespace GameFramework.UI
{
    public class UISubView : UIGameView
    {
        public UIGameView Parent { get; internal set; }
        public ResType ResType => Binding?.ResType ?? ResType.Dynamic;
        internal UIChildDefinition Binding;
    }
}
