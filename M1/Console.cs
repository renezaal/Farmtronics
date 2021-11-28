﻿using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StardewValley;
using StardewModdingAPI;
using StardewValley.Menus;
using StardewValley.BellsAndWhistles;

namespace M1 {
	public class Console : IClickableMenu, IKeyboardSubscriber {
		public new const int width = 800;	// total width/height including the frame
		public new const int height = 640;

		public const int kBackspace = 8;
		public const int kFwdDelete = 127;
		public const int kLeftArrow = 17;
		public const int kRightArrow = 18;
		public const int kUpArrow = 19;
		public const int kDownArrow = 20;
		public const int kTab = 9;
		public const int kControlA = 1;
		public const int kControlC = 3;
		public const int kControlE = 5;

		public bool drawFrame = true;

		public Queue<char> keyBuffer;		// keys pressed but not consumed

		int drawScale = 4;
		Rectangle screenArea;	// "work area" of the actual TV screen, in dialog coordinates

		Texture2D screenOverlay;	// frame and shine
		Rectangle screenSrcR;		// source rectangle for entire screen, including frame
		Rectangle innerSrcR;		// source rectangle for just the display area of the screen
		Texture2D whiteTex;

		Shell owner;
		public TextDisplay display {  get; private set; }

		bool inInputMode;
		RowCol inputStartPos;		// where on the screen we started taking input
		string inputBuf;			// input taken so far
		int inputIndex;				// where in that input buffer the cursor is
		string curSuggestion;		// faded-out text ahead of the cursor for autocomplete
		List<string> history;		// past inputs
		int historyIndex;			// where we are in the history
	
		//bool mouseSelectionEnabled;
		//Vector2 mouseDownPos;
		bool hasSelection;
		RowCol selStart;
		RowCol selEnd;
		
		public Console(Shell owner)
		: base(Game1.uiViewport.Width/2 - width/2, Game1.uiViewport.Height/2 - height/2, width, height) {

			this.owner = owner;

			screenArea = new Rectangle(20*drawScale, 18*drawScale, 160*drawScale, 120*drawScale);	// 640x480 (VGA)!

			screenOverlay = ModEntry.helper.Content.Load<Texture2D>("assets/ScreenOverlay.png");
			screenSrcR = new Rectangle(0, 0, 200, 160);

			innerSrcR = new Rectangle(20, 18, 160, 120);

			display = new TextDisplay();
			display.backColor = new Color(0.31f, 0.11f, 0.86f);
			display.Clear();

			var colors = new Color[] { Color.Red, Color.Yellow, Color.Green, Color.Purple };

			display.SetCursor(19, 1);
			for (int i=0; i<4; i++) {
				display.textColor = colors[i]; display.Print("*");
			}
			display.textColor = Color.Azure; display.Print(" MiniScript M-1 Home Computer ");
			for (int i=0; i<4; i++) {
				display.textColor = colors[3-i]; display.Print("*");
			}
			display.textColor = Color.White;
			display.NextLine(); display.NextLine();
			display.PrintLine("Ready.");
			display.NextLine();
	
			keyBuffer = new Queue<char>();
			history = new List<string>();

			whiteTex = new Texture2D(Game1.graphics.GraphicsDevice, 1, 1);
			whiteTex.SetData(new[] { Color.White });
		}

		public void RemoveFrameAndPositionAt(int left, int top) {
			drawFrame = false;
			xPositionOnScreen = left - screenArea.Left;
			yPositionOnScreen = top - screenArea.Top;
			ModEntry.instance.print($"Console.RemoveFrameAndPositionAt({left}, {top})");

			Game1.keyboardDispatcher.Subscriber = this;
			this.Selected = true;
		}

		public void Present() {
			Game1.player.Halt();
			Game1.activeClickableMenu = this;
			Game1.keyboardDispatcher.Subscriber = this;
			this.Selected = true;
		}

		private void Exit() {
			Game1.exitActiveMenu();
			Game1.player.canMove = true;
		}

		public override void receiveLeftClick(int x, int y, bool playSound = true) {
		}

		public override void receiveRightClick(int x, int y, bool playSound = true) {

		}

		float Clamp(float x, float min, float max) {
			if (x < min) return min;
			if (x > max) return max;
			return x;
		}

		public override void performHoverAction(int x, int y) {
		}

		public override void receiveKeyPress(Keys key) {
			var inp = ModEntry.instance.Helper.Input;
			if (key == Keys.Escape) Exit();
			//Debug.Log($"Console.receiveKeyPress({key}, int {(int)key}) with LeftControl {inp.IsDown(SButton.LeftControl)}, RightControl {inp.IsDown(SButton.RightControl)}");

			// Most keys are handled through one of the misspelled IKeyboardSubscriber
			// interface methods.  But not these:
			switch (key) {
			case Keys.Left:		HandleKey((char)kLeftArrow);		break;
			case Keys.Right:	HandleKey((char)kRightArrow);		break;
			case Keys.Down:		HandleKey((char)kDownArrow);		break;
			case Keys.Up:		HandleKey((char)kUpArrow);			break;
			}

			bool control = inp.IsDown(SButton.LeftControl) || inp.IsDown(SButton.RightControl);
			if (control && key >= Keys.A && key <= Keys.Z) {
				//Debug.Log($"Handling control-{key}");
				if (key == Keys.C && owner.allowControlCBreak) owner.Break();
				else HandleKey((char)(kControlA + (int)key - (int)Keys.A));
			} else {
				//Debug.Log("Not a control key press: {control}, {key}");
			}
		}

		public bool Selected {  get; set; }

		public virtual void RecieveTextInput(char inputChar) {
			HandleKey(inputChar);
		}

		public virtual void RecieveTextInput(string text) {
			foreach (char c in text) HandleKey(c);
		}

		public virtual void RecieveCommandInput(char command) {
			ModEntry.instance.print($"RecieveCommandInput({command}, int {(int)command})");
			switch (command) {
			case '\b':		// backspace
				HandleKey((char)kBackspace);
				break;
			case '\r':		// return/enter
				HandleKey('\n');
				break;
			case '\t':		// tab
				HandleKey('\t');
				break;
			}
		}

		public void RecieveSpecialInput(Keys key) {
			ModEntry.instance.print($"RecieveSpecialInput({key}, int {(int)key})");
		}

		void HandleKey(char keyChar) {
			//ModEntry.instance.print($"HandleKey: {keyChar} ({(int)keyChar})");
			KeyboardState state = Keyboard.GetState();

			if (!inInputMode) {
				// not in input mode, just buffer the keypress till later
				keyBuffer.Enqueue(keyChar);
				return;
			}

			int keyInt = (int)keyChar;
			var inp = ModEntry.instance.Helper.Input;
			bool control = inp.IsDown(SButton.LeftControl) || inp.IsDown(SButton.RightControl);
			bool alt = inp.IsDown(SButton.LeftAlt) || inp.IsDown(SButton.RightAlt);
			bool byWord = control || alt;
			if (keyInt != kTab) ClearAutocomplete();
			if (keyInt == 3 || keyInt == 10 || keyInt == 13) {
				CommitInput();
			} else if ((keyInt == kLeftArrow)) {
				inputIndex = PrevInputStop(inputIndex, byWord);
			} else if (keyInt == kRightArrow) {
				inputIndex = NextInputStop(inputIndex, byWord);
			} else if (keyInt == kControlA) {
				inputIndex = 0;
			} else if (keyInt == kControlE) {
				inputIndex = inputBuf.Length;
			} else if (keyInt == kBackspace) {
				int stop = PrevInputStop(inputIndex, byWord);
				if (stop < inputIndex) {
					inputBuf = inputBuf.Substring(0, stop) + inputBuf.Mid(inputIndex);
					int delCount = inputIndex - stop;
					for (int i=0; i<delCount; i++) display.Backup();
					display.Print(inputBuf.Substring(stop));
					for (int i=0; i<delCount; i++) display.Put(' ');
					inputIndex = stop;
					//if (onInputChanged != null) onInputChanged.Invoke(inputBuf);
				}
			} else if (keyInt == kFwdDelete) {
				int stop = NextInputStop(inputIndex, byWord);
				if (stop > inputIndex) {
					inputBuf = inputBuf.Substring(0, inputIndex) + inputBuf.Mid(stop);
					display.Print(inputBuf.Substring(inputIndex));
					for (int i=0; i<stop-inputIndex; i++) display.Put(' ');
					//if (onInputChanged != null) onInputChanged.Invoke(inputBuf);
				}
			} else if (keyInt == kUpArrow) {
				if (historyIndex <= 0) {
					// No can do
				} else {
					historyIndex--;
					ReplaceInput(history[historyIndex]);
				}
			} else if (keyInt == kDownArrow) {
				if (historyIndex >= history.Count) {
					// No can do				
				} else {
					historyIndex++;
					ReplaceInput(historyIndex < history.Count ? history[historyIndex] : "");
				}
			} else if (keyInt == kTab) {
				if (curSuggestion != null) {
					inputBuf += curSuggestion;
					inputIndex += curSuggestion.Length;
					display.Print(curSuggestion);
				}
			} else {
				if (string.IsNullOrEmpty(inputBuf)) inputBuf = keyChar.ToString();
				else inputBuf = inputBuf.Substring(0, inputIndex) + keyChar.ToString() + inputBuf.Substring(inputIndex);
				display.Print(inputBuf.Substring(inputIndex));
				inputIndex++;			
				//if (onInputChanged != null) onInputChanged.Invoke(inputBuf);
			}
		
			if (inInputMode) SetCursorForInput();
		}

		public override void update(GameTime time) {
			base.update(time);
			owner.Update(time);
			display.Update(time);
		}

		
		public void NoteScrolled() {
			inputStartPos.row++;
		}
	
		public void CommitInput() {
			//Debug.Log("Committing input: " + inputBuf);
			ClearSelection();
			inputIndex = inputBuf.Length;
			SetCursorForInput(false);
			if (!string.IsNullOrEmpty(inputBuf)) history.Add(inputBuf);
			display.HideCursor();
			display.NextLine();		
			inInputMode = false;

			if (owner != null) owner.HandleCommand(inputBuf);
		}
	
		public void StartInput() {
			inputStartPos = display.GetCursor();
			ClearSelection();
			display.ShowCursor();
			inputBuf = "";
			inputIndex = 0;
			inInputMode = true;
			historyIndex = history.Count;
			//if (onInputChanged != null) onInputChanged.Invoke(inputBuf);
		}
	
		public bool InputInProgress() {
			return inInputMode;
		}
	
		public void AbortInput() {
			inInputMode = false;
		}

		public void Beep() {
			//audio.clip = systemBeep;
			//audio.Play();
		}
	
		public void TypeInput(string s) {
			foreach (char c in s) HandleKey(c);
		}

		
		void ClearAutocomplete() {
			if (string.IsNullOrEmpty(curSuggestion)) return;
			RowCol pos = display.GetCursor();
			for (int i=0; i<curSuggestion.Length; i++) display.Put(' ');
			curSuggestion = null;
			display.SetCursor(pos);
		}
	
		void ReplaceInput(string newInput) {
			display.SetCursor(inputStartPos);
			for (int i=0; i<inputBuf.Length; i++) display.Put(' ');
			inputBuf = newInput;
			display.SetCursor(inputStartPos);
			display.Print(inputBuf);
			inputIndex = inputBuf.Length;
			SetCursorForInput();
		}
	
		void SetCursorForInput(bool showSuggestion=true) {
			int curRow = inputStartPos.row;
			int curCol = inputStartPos.col + inputIndex;
			while (curCol >= display.cols) {
				curRow--;
				curCol -= display.cols;
			}
			display.SetCursor(curRow, curCol);

			if (!showSuggestion) return;
			curSuggestion = null;
			// ToDo: Autocomplete
			//if (autocompCallback != null && inputIndex == inputBuf.Length) {
			//	curSuggestion = autocompCallback(inputBuf);
			//}
			if (curSuggestion != null) {
				Color c = display.textColor;
				display.textColor = Color.Lerp(c, display.backColor, 0.75f);
				display.Print(curSuggestion);
				display.textColor = c;
				for (int i=0; i<curSuggestion.Length; i++) display.Backup();
				display.SetCursor(curRow, curCol);	// (again, after above printing)
				//Debug.Log("Showed autocomp: " + curSuggestion);
			}
		}
	
		int PrevInputStop(int index, bool byWord) {
			if (index <= 0) return index;
			index--;
			if (byWord) {
				// skip any nonword characters; then continue till we get to nonword chars again
				/*  ToDo
				bool numeric = false;
				while (!Miniscript.CodeEditor.IsTokenChar(inputBuf[index], ref numeric) && index > 0) {
					index--;
				}
				while (index > 0 && Miniscript.CodeEditor.IsTokenChar(inputBuf[index-1], ref numeric)) {
					index--;
				}
				*/
			}
			return index;
		}
	
		int NextInputStop(int index, bool byWord) {
			int maxi = inputBuf.Length;
			if (index >= maxi) return index;
			index++;
			if (byWord) {
				// skip any nonword characters; then continue till we get to nonword chars again
				/* ToDo
				bool numeric = false;
				while (index < maxi && !Miniscript.CodeEditor.IsTokenChar(inputBuf[index], ref numeric)) {
					index++;
				}
				while (index < maxi && Miniscript.CodeEditor.IsTokenChar(inputBuf[index-1], ref numeric)) {
					index++;
				}
				*/
			}
			return index;
		}
	
		void UpdateSelection() {
			/* ToDo
			if (!mouseSelectionEnabled) return;
			if (Input.GetMouseButtonDown(0)) {
				ClearSelection();
				Vector3 worldPos = Camera.main.ScreenToWorldPoint(Input.mousePosition);
				selStart = selEnd = display.RowColForXY(display.transform.InverseTransformPoint(worldPos));
				mouseDownPos = Input.mousePosition;
				//Debug.Log("You clicked: " + selStart);
			}
			if (Input.GetMouseButton(0)) {
				if (Vector2.Distance(Input.mousePosition, mouseDownPos) < 10) {
					ClearSelection();
					selEnd = selStart;
					return;
				}
				Vector3 worldPos = Camera.main.ScreenToWorldPoint(Input.mousePosition);
				var newEnd = display.RowColForXY(display.transform.InverseTransformPoint(worldPos));
				if (newEnd != selEnd) SetSelection(selStart, newEnd);
			}
			*/
		}
	
		void ClearSelection() {
			if (!hasSelection) return;
			InvertRange(selStart, selEnd);
			hasSelection = false;
		}
	
		void SetSelection(RowCol startPos, RowCol endPos) {
			if (hasSelection) InvertRange(selStart, selEnd);
			selStart = startPos;
			selEnd = endPos;
			InvertRange(startPos, endPos);
			hasSelection = true;
		}
	
		void InvertRange(RowCol startPos, RowCol endPos) {
			if (startPos.row < endPos.row || (startPos.row == endPos.row && startPos.col > endPos.col)) {
				var temp = startPos;
				startPos = endPos;
				endPos = temp;
			}
			RowCol pos = startPos;
			while (pos != endPos && pos.row >= 0) {
				var cell = display.Get(pos.row, pos.col);
				cell.inverse = !cell.inverse;
				//display.UpdateCell(cell);
				pos.col++;
				if (pos.col >= display.cols) {
					pos.col = 0;
					pos.row--;
				}
			}
		}
	
		string GetTextInRange(RowCol startPos, RowCol endPos) {
			if (startPos.row < endPos.row || (startPos.row == endPos.row && startPos.col > endPos.col)) {
				var temp = startPos;
				startPos = endPos;
				endPos = temp;
			}
			RowCol pos = startPos;
			var result = new System.Text.StringBuilder();
			System.Text.StringBuilder pendingSpaces = null;
			while (pos != endPos && pos.row >= 0) {
				var cell = display.Get(pos.row, pos.col);
				if (cell == null || cell.character == ' ') {
					// got a space: don't append this to our result just yet
					if (pendingSpaces == null) pendingSpaces = new System.Text.StringBuilder();
					pendingSpaces.Append(" ");
				} else {
					// not a space; append any pending spaces, and then this character.
					if (pendingSpaces != null) result.Append(pendingSpaces.ToString());
					result.Append(cell.character.ToString());
					pendingSpaces = null;
				}
				pos.col++;
				if (pos.col >= display.cols) {
					pos.col = 0;
					pos.row--;
					result.Append(System.Environment.NewLine);
					pendingSpaces = null;
				}
			}
			return result.ToString();
		}
	
		void DrawInDialogCoords(SpriteBatch b, Texture2D srcTex, Rectangle srcRect, float left, float top, float layerDepth=0.95f) {
			b.Draw(srcTex, new Vector2(xPositionOnScreen + left, yPositionOnScreen + top), srcRect, Color.White,
				0, Vector2.Zero, drawScale, SpriteEffects.None, layerDepth);
		}

		void DrawInDialogCoords(SpriteBatch b, Texture2D srcTex, Rectangle srcRect, Vector2 topLeft, float layerDepth=0.95f) {
			DrawInDialogCoords(b, srcTex, srcRect, topLeft.X, topLeft.Y, layerDepth);
		}


	
		public override void draw(SpriteBatch b) {

			Vector2 positionOnScreen = new Vector2(xPositionOnScreen, yPositionOnScreen);

			Rectangle displayArea;
			if (drawFrame) {
				// fade out the background
				b.Draw(Game1.fadeToBlackRect, new Rectangle(0, 0, Game1.uiViewport.Width, Game1.uiViewport.Height), Color.Black * 0.5f);
	
				// draw the screen background
				displayArea = new Rectangle(xPositionOnScreen + screenArea.Left,
					yPositionOnScreen + screenArea.Top, screenArea.Width, screenArea.Height);

				Rectangle border = new Rectangle(displayArea.Left - 32, displayArea.Top - 32, displayArea.Width + 64, displayArea.Height + 64);
				FillRect(b, border, new Color(0.64f, 0.57f, 0.98f));
			} else {
				// draw just the inner portion of the screen background
				displayArea = new Rectangle(xPositionOnScreen, yPositionOnScreen, screenArea.Width, screenArea.Height);
			}
			
			// draw content
			FillRect(b, displayArea, display.backColor);
			display.Render(b, displayArea);

			
			// draw bezel/shine on top
			b.Draw(screenOverlay, positionOnScreen, drawFrame ? screenSrcR : innerSrcR,
				Color.White,
				0, Vector2.Zero, drawScale, SpriteEffects.None, 0.5f);
		}

		void FillRect(SpriteBatch b, Rectangle rect, Color color) {
			b.Draw(whiteTex, rect, color);
		}
	}
}
