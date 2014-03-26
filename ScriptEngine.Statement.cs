/*
Copyright (c) 2014 Darren Horrocks

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
*/

using System;

namespace DScript
{
	public partial class ScriptEngine
	{
		private void Statement(bool execute)
		{
			if (_currentLexer.TokenType == ScriptLex.LexTypes.Id ||
			    _currentLexer.TokenType == ScriptLex.LexTypes.Int ||
			    _currentLexer.TokenType == ScriptLex.LexTypes.Float ||
			    _currentLexer.TokenType == ScriptLex.LexTypes.Str ||
			    _currentLexer.TokenType == (ScriptLex.LexTypes) '-')
			{
				//execite a basic statement
				Clean(Base(execute));
				_currentLexer.Match((ScriptLex.LexTypes)';');
			}
			else if (_currentLexer.TokenType == (ScriptLex.LexTypes)'{')
			{
				//code block
				Block(execute);
			}
			else if (_currentLexer.TokenType == (ScriptLex.LexTypes)';')
			{
				//allow for multiple semi colon such as ;;;
				_currentLexer.Match((ScriptLex.LexTypes)';');
			}
			else if (_currentLexer.TokenType == ScriptLex.LexTypes.RVar)
			{
				//creating variables
				//TODO: make this less shit

				_currentLexer.Match(ScriptLex.LexTypes.RVar);

				while (_currentLexer.TokenType != (ScriptLex.LexTypes) ';')
				{
					ScriptVarLink a = null;
					if (execute)
					{
						a = _scopes[_scopes.Count - 1].FindChildOrCreate(_currentLexer.TokenString);
					}

					_currentLexer.Match(ScriptLex.LexTypes.Id);

					//get through the dots
					while (_currentLexer.TokenType == (ScriptLex.LexTypes)'.')
					{
						_currentLexer.Match((ScriptLex.LexTypes)'.');
						if (execute)
						{
							ScriptVarLink aLast = a;
							a = aLast.Var.FindChildOrCreate(_currentLexer.TokenString);
						}

						_currentLexer.Match(ScriptLex.LexTypes.Id);
					}

					//initialiser
					if (_currentLexer.TokenType == (ScriptLex.LexTypes)'=')
					{
						_currentLexer.Match((ScriptLex.LexTypes)'=');
						ScriptVarLink varLink = Base(execute);
						if (execute)
						{
							a.ReplaceWith(varLink);
						}
						Clean(varLink);
					}

					if (_currentLexer.TokenType != (ScriptLex.LexTypes)';')
					{
						_currentLexer.Match((ScriptLex.LexTypes)',');
					}
				}

				_currentLexer.Match((ScriptLex.LexTypes)';');
			}
			else if (_currentLexer.TokenType == ScriptLex.LexTypes.RIf)
			{
				//if condition
				_currentLexer.Match(ScriptLex.LexTypes.RIf);
				_currentLexer.Match((ScriptLex.LexTypes)'(');
				ScriptVarLink varLink = Base(execute);
				_currentLexer.Match((ScriptLex.LexTypes)')');

				bool condition = execute && varLink.Var.GetBool();
				Statement(condition);

				if (_currentLexer.TokenType == ScriptLex.LexTypes.RElse)
				{
					//else part of an if
					_currentLexer.Match(ScriptLex.LexTypes.RElse);

					Statement(!condition);
				}
			}
			else if (_currentLexer.TokenType == ScriptLex.LexTypes.RWhile)
			{
				//while loop
				_currentLexer.Match(ScriptLex.LexTypes.RWhile);
				_currentLexer.Match((ScriptLex.LexTypes)'(');

				Int32 whileConditionStart = _currentLexer.TokenStart;
				ScriptVarLink condition = Base(execute);
				bool loopCondition = execute && condition.Var.GetBool();

				Clean(condition);

				ScriptLex whileCond = _currentLexer.GetSubLex(whileConditionStart);
				_currentLexer.Match((ScriptLex.LexTypes)')');

				Int32 whileBodyStart = _currentLexer.TokenStart;

				Statement(loopCondition);

				ScriptLex whileBody = _currentLexer.GetSubLex(whileBodyStart);
				ScriptLex oldLex = _currentLexer;

				//TODO: possible maximum itteration limit?
				while (loopCondition)
				{
					whileCond.Reset();
					
					_currentLexer = whileCond;
					
					condition = Base(true);
					
					loopCondition = condition.Var.GetBool();

					Clean(condition);

					if (loopCondition)
					{
						whileBody.Reset();
						_currentLexer = whileBody;
						Statement(true);
					}
				}

				_currentLexer = oldLex;

				whileCond.Dispose();
				whileBody.Dispose();
			}
			else if (_currentLexer.TokenType == ScriptLex.LexTypes.RFor)
			{
				/*
        int loopCount = TINYJS_LOOP_MAX_ITERATIONS;
        while (execute && loopCond && loopCount-->0) {
            if (execute && loopCond) {
                forIter->reset();
                l = forIter;
                CLEAN(base(execute));
            }
        }
*/
				//for loop
				_currentLexer.Match(ScriptLex.LexTypes.RFor);
				_currentLexer.Match((ScriptLex.LexTypes)'(');

				Statement(execute); //init

				int forConditionStart = _currentLexer.TokenStart;
				ScriptVarLink condition = Base(execute);
				bool loopCondition = execute && condition.Var.GetBool();

				Clean(condition);

				ScriptLex forCondition = _currentLexer.GetSubLex(forConditionStart);

				_currentLexer.Match((ScriptLex.LexTypes)';');

				int forIterStart = _currentLexer.TokenStart;

				Clean(Base(false));

				ScriptLex forIter = _currentLexer.GetSubLex(forIterStart);

				_currentLexer.Match((ScriptLex.LexTypes)')');

				int forBodyStart = _currentLexer.TokenStart;

				Statement(loopCondition);

				ScriptLex forBody = _currentLexer.GetSubLex(forBodyStart);
				ScriptLex oldLex = _currentLexer;
				if (loopCondition)
				{
					forIter.Reset();
					_currentLexer = forIter;

					Clean(Base(true));
				}

				//TODO: limit number of iterations?
				while (execute && loopCondition)
				{
					forCondition.Reset();
					_currentLexer = forCondition;

					condition = Base(true);

					loopCondition = condition.Var.GetBool();

					Clean(condition);

					if (loopCondition)
					{
						forBody.Reset();
						_currentLexer = forBody;

						Statement(true);
					}

					if (loopCondition)
					{
						forIter.Reset();
						_currentLexer = forIter;

						Clean(Base(true));
					}
				}

				_currentLexer = oldLex;
				forCondition.Dispose();
				forIter.Dispose();
				forBody.Dispose();
			}
			else if (_currentLexer.TokenType == ScriptLex.LexTypes.RReturn)
			{
				_currentLexer.Match(ScriptLex.LexTypes.RReturn);

				ScriptVarLink res = null;
				if (_currentLexer.TokenType != (ScriptLex.LexTypes)';')
				{
					res = Base(execute);
				}
				if (execute)
				{
					ScriptVarLink resultVar = _scopes[_scopes.Count - 1].FindChild(ScriptVar.ReturnVarName);
					if (resultVar != null)
					{
						resultVar.ReplaceWith(res);
					}
					else
					{
						//return statement outside of function???
					}
				}

				Clean(res);

				execute = false;
				_currentLexer.Match((ScriptLex.LexTypes)';');
			}
			else if (_currentLexer.TokenType == ScriptLex.LexTypes.RFunction)
			{
				//function
				ScriptVarLink funcVar = ParseFunctionDefinition();
				if (execute)
				{
					if (funcVar.Name == String.Empty)
					{
						//functions must have a name at statement level
					}
					else
					{
						_scopes[_scopes.Count - 1].AddChildNoDup(funcVar.Name, funcVar.Var);
					}
				}

				Clean(funcVar);
			}
			else
			{
				_currentLexer.Match(ScriptLex.LexTypes.Eof);
			}
		}
	}
}
