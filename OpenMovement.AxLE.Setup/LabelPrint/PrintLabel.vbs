' Based on BcdLabel.vbs example
'<SCRIPT LANGUAGE="VBScript">

' wscript "Label.vbs" "01:23:45:67:89:AB"
' System call from C:  system("wscript \"Label.vbs\" \"01:23:45:67:89:AB\"");

	'Wscript.Echo "PrintLabel.vbs"

	' Name and code from arguments
	set Args = Wscript.Arguments
	If Args.count < 2 Then
		Wscript.Echo "ERROR: Insufficient arguments supplied"
		wscript.quit(1)
	End If

	' Print
	'Wscript.Echo "Printing..."
	DoPrint(Args(0))
	'Wscript.Echo "Done"

	Sub DoPrint(strFilePath)
	
		Set ObjDoc = CreateObject("bpac.Document")
		bRet = ObjDoc.Open(strFilePath)
		If (bRet <> False) Then
			'Wscript.Echo "Got document..."
			
			'ObjDoc.GetObject("objName").Text = Args(1)
			'ObjDoc.GetObject("objBarcode").Text = Args(1)
			
			For i = 1 To Args.Count - 1 Step 2
				'Wscript.Echo "" & Args(i) & " -> " & Args(i + 1)
				ObjDoc.GetObject(Args(i)).Text = Args(i + 1)
			Next
			
			' ObjDoc.SetMediaByName ObjDoc.Printer.GetMediaName(), True
			ObjDoc.StartPrint "", 0
			ObjDoc.PrintOut 1, 0
			ObjDoc.EndPrint
			ObjDoc.Close
			'Wscript.Echo "Finished..."
		Else
			'Wscript.Echo "Problem getting document."
		End If
		Set ObjDoc = Nothing
	End Sub
