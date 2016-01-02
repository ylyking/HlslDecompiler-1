﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace HlslDecompiler
{
    public class HlslWriter
    {
        ShaderModel shader;

        FileStream hlslFile;
        StreamWriter hlslWriter;
        string indent = "";

        int numInputs, numOutputs;

        public ICollection<Constant> ConstantDefinitions { get; private set; }
        public ICollection<ConstantInt> ConstantIntDefinitions { get; private set; }
        public ICollection<ConstantDeclaration> ConstantDeclarations { get; private set; }
        public ICollection<RegisterDeclaration> RegisterDeclarations { get; private set; }

        public HlslWriter(ShaderModel shader)
        {
            this.shader = shader;
        }

        static int MakeFourCC(string id)
        {
            if (BitConverter.IsLittleEndian)
            {
                return (id[0]) + (id[1] << 8) + (id[2] << 16) + (id[3] << 24);
            }
            return (id[3]) + (id[2] << 8) + (id[1] << 16) + (id[0] << 24);
        }

        void WriteLine()
        {
            hlslWriter.WriteLine();
        }

        void WriteLine(string value)
        {
            hlslWriter.Write(indent);
            hlslWriter.WriteLine(value);
        }

        void WriteLine(string format, params object[] args)
        {
            hlslWriter.Write(indent);
            hlslWriter.WriteLine(format, args);
        }

        static string ReadStringNullTerminated(Stream stream)
        {
            StringBuilder builder = new StringBuilder();
            char b;
            while ((b = (char)stream.ReadByte()) != 0)
            {
                builder.Append(b.ToString());
            }
            return builder.ToString();
        }

        void ParseConstantTable(Instruction ctabComment)
        {
            byte[] constantTable = new byte[ctabComment.Params.Length * 4];
            for (int i = 1; i < ctabComment.Params.Length; i++)
            {
                constantTable[i * 4 - 4] = (byte)(ctabComment.Params[i] & 0xFF);
                constantTable[i * 4 - 3] = (byte)((ctabComment.Params[i] >> 8) & 0xFF);
                constantTable[i * 4 - 2] = (byte)((ctabComment.Params[i] >> 16) & 0xFF);
                constantTable[i * 4 - 1] = (byte)((ctabComment.Params[i] >> 24) & 0xFF);
            }

            var ctabStream = new MemoryStream(constantTable);
            using (var ctabReader = new BinaryReader(ctabStream))
            {
                int ctabSize = ctabReader.ReadInt32();
                System.Diagnostics.Debug.Assert(ctabSize == 0x1C);
                long creatorPosition = ctabReader.ReadInt32();

                int minorVersion = ctabReader.ReadByte();
                int majorVersion = ctabReader.ReadByte();
                System.Diagnostics.Debug.Assert(majorVersion == shader.MajorVersion);
                System.Diagnostics.Debug.Assert(minorVersion == shader.MinorVersion);

                var shaderType = (ShaderType)ctabReader.ReadUInt16();
                System.Diagnostics.Debug.Assert(shaderType == shader.Type);

                int numConstants = ctabReader.ReadInt32();
                long constantInfoPosition = ctabReader.ReadInt32();
                ShaderFlags shaderFlags = (ShaderFlags)ctabReader.ReadInt32();
                Console.WriteLine("Flags = {0}", shaderFlags);

                long shaderModelPosition = ctabReader.ReadInt32();
                //Console.WriteLine("ctabStart = {0}, shaderModelPosition = {1}", ctabStart, shaderModelPosition);


                ctabStream.Position = creatorPosition;
                string compilerInfo = ReadStringNullTerminated(ctabStream);
                Console.WriteLine(compilerInfo);

                ctabStream.Position = shaderModelPosition;
                string shaderModel = ReadStringNullTerminated(ctabStream);
                Console.WriteLine(shaderModel);


                for (int i = 0; i < numConstants; i++)
                {
                    ctabStream.Position = constantInfoPosition + i * 20;

                    // D3DXSHADER_CONSTANTINFO
                    int nameOffset = ctabReader.ReadInt32();
                    RegisterSet registerSet = (RegisterSet)ctabReader.ReadInt16();
                    short registerIndex = ctabReader.ReadInt16();
                    short registerCount = ctabReader.ReadInt16();
                    ctabStream.Position += sizeof(short); // Reserved
                    int typeInfoOffset = ctabReader.ReadInt32();
                    int defaultValueOffset = ctabReader.ReadInt32();
                    System.Diagnostics.Debug.Assert(defaultValueOffset == 0);

                    ctabStream.Position = nameOffset;
                    string name = ReadStringNullTerminated(ctabStream);

                    // D3DXSHADER_TYPEINFO
                    ctabStream.Position = typeInfoOffset;
                    ParameterClass cl = (ParameterClass)ctabReader.ReadInt16();
                    ParameterType type = (ParameterType)ctabReader.ReadInt16();
                    short rows = ctabReader.ReadInt16();
                    short columns = ctabReader.ReadInt16();
                    short numElements = ctabReader.ReadInt16();
                    short numStructMembers = ctabReader.ReadInt16();
                    int structMemberInfoOffset = ctabReader.ReadInt32();
                    //System.Diagnostics.Debug.Assert(numElements == 1);
                    System.Diagnostics.Debug.Assert(structMemberInfoOffset == 0);

                    var declaration = new ConstantDeclaration(name, registerSet, registerIndex, registerCount, cl, type, rows, columns);
                    ConstantDeclarations.Add(declaration);
                }
            }

            foreach (ConstantDeclaration declaration in ConstantDeclarations)
            {
                string typeName = GetTypeName(declaration);
                WriteLine("{0} {1};", typeName, declaration.Name);
            }
            if (ConstantDeclarations.Count != 0)
            {
                WriteLine();
            }
        }

        static string ApplyModifier(SourceModifier modifier, string value)
        {
            switch (modifier)
            {
                case SourceModifier.None:
                    return value;
                case SourceModifier.Negate:
                    return string.Format("-{0}", value);
                case SourceModifier.Bias:
                    return string.Format("{0}_bias", value);
                case SourceModifier.BiasAndNegate:
                    return string.Format("-{0}_bias", value);
                case SourceModifier.Sign:
                    return string.Format("{0}_bx2", value);
                case SourceModifier.SignAndNegate:
                    return string.Format("-{0}_bx2", value);
                case SourceModifier.Complement:
                    throw new NotImplementedException();
                case SourceModifier.X2:
                    return string.Format("(2 * {0})", value);
                case SourceModifier.X2AndNegate:
                    return string.Format("(-2 * {0})", value);
                case SourceModifier.DivideByZ:
                    return string.Format("{0}_dz", value);
                case SourceModifier.DivideByW:
                    return string.Format("{0}_dw", value);
                case SourceModifier.Abs:
                    return string.Format("abs({0})", value);
                case SourceModifier.AbsAndNegate:
                    return string.Format("-abs({0})", value);
                case SourceModifier.Not:
                    throw new NotImplementedException();
                default:
                    throw new NotImplementedException();
            }
        }

        string GetRegisterName(Instruction instruction, int paramIndex)
        {
            RegisterType registerType = instruction.GetParamRegisterType(paramIndex);
            int registerNumber = instruction.GetParamRegisterNumber(paramIndex);

            var decl = RegisterDeclarations.FirstOrDefault(x => x.RegisterType == registerType && x.RegisterNumber == registerNumber);
            if (decl != null)
            {
                switch (registerType)
                {
                    case RegisterType.Texture:
                        return decl.Name;
                    case RegisterType.Input:
                        return (numInputs == 1) ? decl.Name : ("i." + decl.Name);
                    case RegisterType.Output:
                        return (numOutputs == 1) ? "o" : ("o." + decl.Name);
                    case RegisterType.Sampler:
                        var samplerDecl = ConstantDeclarations.FirstOrDefault(x => x.RegisterSet == RegisterSet.Sampler && x.RegisterIndex == registerNumber);
                        if (samplerDecl != null)
                        {
                            return samplerDecl.Name;
                        }
                        else
                        {
                            throw new NotImplementedException();
                        }
                    case RegisterType.MiscType:
                        if (registerNumber == 0)
                        {
                            return "vFace";
                        }
                        else if (registerNumber == 1)
                        {
                            return "vPos";
                        }
                        else
                        {
                            throw new NotImplementedException();
                        }
                    default:
                        throw new NotImplementedException();
                }
            }

            switch (registerType)
            {
                case RegisterType.Const:
                    var constDecl = ConstantDeclarations.FirstOrDefault(x => x.ContainsIndex(registerNumber));
                    if (constDecl != null)
                    {
                        throw new NotImplementedException();
                    }
                    break;
                case RegisterType.ColorOut:
                    return "o";
            }

            return null;
        }

        int GetRegisterFullLength(Instruction instruction, int paramIndex)
        {
            RegisterType registerType = instruction.GetParamRegisterType(paramIndex);
            int registerNumber = instruction.GetParamRegisterNumber(paramIndex);
            var decl = RegisterDeclarations.FirstOrDefault(x => x.RegisterType == registerType && x.RegisterNumber == registerNumber);
            if (decl != null)
            {
                if (decl.TypeName == "float")
                {
                    return 1;
                }
                else if (decl.TypeName == "float2")
                {
                    return 2;
                }
                else if (decl.TypeName == "float3")
                {
                    return 3;
                }
            }
            return 4;
        }

        string GetDestinationName(Instruction instruction)
        {
            int destIndex = instruction.GetDestinationParamIndex();

            string registerName = GetRegisterName(instruction, destIndex);
            registerName = registerName ?? instruction.GetParamRegisterName(destIndex);
            int registerLength = GetRegisterFullLength(instruction, destIndex);
            string writeMaskName = instruction.GetDestinationWriteMaskName(registerLength, true);

            return string.Format("{0}{1}", registerName, writeMaskName);
        }

        string GetSourceConstantName(Instruction instruction, int srcIndex)
        {
            var registerType = instruction.GetParamRegisterType(srcIndex);
            int registerNumber = instruction.GetParamRegisterNumber(srcIndex);

            switch (registerType)
            {
                case RegisterType.ConstBool:
                    //throw new NotImplementedException();
                    return null;
                case RegisterType.ConstInt:
                    {
                        var constantInt = ConstantIntDefinitions.FirstOrDefault(x => x.RegisterIndex == registerNumber);
                        if (constantInt == null)
                        {
                            return null;
                        }
                        byte[] swizzle = instruction.GetSourceSwizzleComponents(srcIndex);
                        uint[] constant = new uint[] {
                                constantInt[swizzle[0]],
                                constantInt[swizzle[1]],
                                constantInt[swizzle[2]],
                                constantInt[swizzle[3]] };

                        switch (instruction.GetSourceModifier(srcIndex))
                        {
                            case SourceModifier.None:
                                break;
                            case SourceModifier.Negate:
                                throw new NotImplementedException();
                                /*
                                for (int i = 0; i < 4; i++)
                                {
                                    constant[i] = -constant[i];
                                }*/
                                break;
                            default:
                                throw new NotImplementedException();
                        }

                        int destLength = instruction.GetDestinationMaskLength();
                        switch (destLength)
                        {
                            case 1:
                                return constant[0].ToString();
                            case 2:
                                if (constant[0] == constant[1])
                                {
                                    return constant[0].ToString();
                                }
                                return string.Format("int2({0}, {1})", constant[0], constant[1]);
                            case 3:
                                if (constant[0] == constant[1] && constant[0] == constant[2])
                                {
                                    return constant[0].ToString();
                                }
                                return string.Format("int3({0}, {1}, {2})",
                                    constant[0].ToString(),
                                    constant[1].ToString(),
                                    constant[2].ToString());
                            case 4:
                                if (constant[0] == constant[1] && constant[0] == constant[2] && constant[0] == constant[3])
                                {
                                    return constant[0].ToString();
                                }
                                return string.Format("int4({0}, {1}, {2}, {3})",
                                    constant[0].ToString(),
                                    constant[1].ToString(),
                                    constant[2].ToString(),
                                    constant[3].ToString());
                            default:
                                throw new InvalidOperationException();
                        }
                    }

                case RegisterType.Const:
                case RegisterType.Const2:
                case RegisterType.Const3:
                case RegisterType.Const4:
                    {
                        var constantRegister = ConstantDefinitions.FirstOrDefault(x => x.RegisterIndex == registerNumber);
                        if (constantRegister == null)
                        {
                            return null;
                        }

                        byte[] swizzle = instruction.GetSourceSwizzleComponents(srcIndex);
                        float[] constant = new float[] {
                            constantRegister[swizzle[0]],
                            constantRegister[swizzle[1]],
                            constantRegister[swizzle[2]],
                            constantRegister[swizzle[3]] };

                        switch (instruction.GetSourceModifier(srcIndex))
                        {
                            case SourceModifier.None:
                                break;
                            case SourceModifier.Negate:
                                for (int i = 0; i < 4; i++)
                                {
                                    constant[i] = -constant[i];
                                }
                                break;
                            default:
                                throw new NotImplementedException();
                        }

                        int destLength;
                        if (instruction.HasDestination)
                        {
                            destLength = instruction.GetDestinationMaskLength();
                        }
                        else
                        {
                            if (instruction.Opcode == Opcode.If ||instruction.Opcode == Opcode.IfC)
                            {
                                // TODO
                            }
                            destLength = 4;
                        }
                        switch (destLength)
                        {
                            case 1:
                                return constant[0].ToString(System.Globalization.CultureInfo.InvariantCulture);
                            case 2:
                                if (constant[0] == constant[1])
                                {
                                    return constant[0].ToString(System.Globalization.CultureInfo.InvariantCulture);
                                }
                                return string.Format("float2({0}, {1})",
                                    constant[0].ToString(System.Globalization.CultureInfo.InvariantCulture),
                                    constant[1].ToString(System.Globalization.CultureInfo.InvariantCulture));
                            case 3:
                                if (constant[0] == constant[1] && constant[0] == constant[2])
                                {
                                    return constant[0].ToString(System.Globalization.CultureInfo.InvariantCulture);
                                }
                                return string.Format("float3({0}, {1}, {2})",
                                    constant[0].ToString(System.Globalization.CultureInfo.InvariantCulture),
                                    constant[1].ToString(System.Globalization.CultureInfo.InvariantCulture),
                                    constant[2].ToString(System.Globalization.CultureInfo.InvariantCulture));
                            case 4:
                                if (constant[0] == constant[1] && constant[0] == constant[2] && constant[0] == constant[3])
                                {
                                    return constant[0].ToString(System.Globalization.CultureInfo.InvariantCulture);
                                }
                                return string.Format("float4({0}, {1}, {2}, {3})",
                                    constant[0].ToString(System.Globalization.CultureInfo.InvariantCulture),
                                    constant[1].ToString(System.Globalization.CultureInfo.InvariantCulture),
                                    constant[2].ToString(System.Globalization.CultureInfo.InvariantCulture),
                                    constant[3].ToString(System.Globalization.CultureInfo.InvariantCulture));
                            default:
                                throw new InvalidOperationException();
                        }
                    }
                default:
                    return null;
            }
        }

        string GetSourceName(Instruction instruction, int srcIndex)
        {
            string sourceRegisterName;

            var registerType = instruction.GetParamRegisterType(srcIndex);
            switch (registerType)
            {
                case RegisterType.Const:
                case RegisterType.Const2:
                case RegisterType.Const3:
                case RegisterType.Const4:
                case RegisterType.ConstBool:
                case RegisterType.ConstInt:
                    sourceRegisterName = GetSourceConstantName(instruction, srcIndex);
                    if (sourceRegisterName != null)
                    {
                        return sourceRegisterName;
                    }

                    ParameterType parameterType;
                    switch (registerType)
                    {
                        case RegisterType.Const:
                        case RegisterType.Const2:
                        case RegisterType.Const3:
                        case RegisterType.Const4:
                            parameterType = ParameterType.Float;
                            break;
                        case RegisterType.ConstBool:
                            parameterType = ParameterType.Bool;
                            break;
                        case RegisterType.ConstInt:
                            parameterType = ParameterType.Int;
                            break;
                        default:
                            throw new NotImplementedException();
                    }
                    int registerNumber = instruction.GetParamRegisterNumber(srcIndex);
                    var decl = ConstantDeclarations.FirstOrDefault(
                        x => x.ParameterType == parameterType && x.ContainsIndex(registerNumber));
                    if (decl == null)
                    {
                        // Constant register not found in def statements nor the constant table
                        throw new NotImplementedException();
                    }

                    if (decl.ParameterClass == ParameterClass.MatrixRows)
                    {
                        sourceRegisterName = string.Format("{0}[{1}]", decl.Name, registerNumber - decl.RegisterIndex);
                    }
                    else
                    {
                        sourceRegisterName = decl.Name;
                    }
                    break;
                default:
                    sourceRegisterName = GetRegisterName(instruction, srcIndex);
                    break;
            }

            sourceRegisterName = sourceRegisterName ?? instruction.GetParamRegisterName(srcIndex);

            sourceRegisterName += instruction.GetSourceSwizzleName(srcIndex);
            return ApplyModifier(instruction.GetSourceModifier(srcIndex), sourceRegisterName);
        }

        string GetTypeName(ConstantDeclaration declaration)
        {
            switch (declaration.ParameterClass)
            {
                case ParameterClass.Scalar:
                    return declaration.ParameterType.ToString().ToLower();
                case ParameterClass.Vector:
                    if (declaration.ParameterType == ParameterType.Float)
                    {
                        return "float" + declaration.Columns;
                    }
                    else
                    {
                        throw new NotImplementedException();
                    }
                case ParameterClass.MatrixColumns:
                case ParameterClass.MatrixRows:
                    if (declaration.ParameterType == ParameterType.Float)
                    {
                        return string.Format("float{0}x{1}", declaration.Rows, declaration.Columns);
                    }
                    else
                    {
                        throw new NotImplementedException();
                    }
                case ParameterClass.Object:
                    switch (declaration.ParameterType)
                    {
                        case ParameterType.Sampler2D:
                        case ParameterType.Sampler3D:
                            return "sampler";
                        default:
                            throw new NotImplementedException();
                    }
            }
            throw new NotImplementedException();
        }

        void WriteInstruction(Instruction instruction)
        {
            switch (instruction.Opcode)
            {
                case Opcode.Abs:
                    WriteLine("{0} = abs({1});", GetDestinationName(instruction),
                        GetSourceName(instruction, 1));
                    break;
                case Opcode.Add:
                    WriteLine("{0} = {1} + {2};", GetDestinationName(instruction),
                        GetSourceName(instruction, 1), GetSourceName(instruction, 2));
                    break;
                case Opcode.Cmp:
                    // TODO: should be per-component
                    WriteLine("{0} = ({1} >= 0) ? {2} : {3};", GetDestinationName(instruction),
                        GetSourceName(instruction, 1), GetSourceName(instruction, 2), GetSourceName(instruction, 3));
                    break;
                case Opcode.Def:
                    var c = new Constant(
                        instruction.GetParamRegisterNumber(0),
                        instruction.GetParamSingle(1),
                        instruction.GetParamSingle(2),
                        instruction.GetParamSingle(3),
                        instruction.GetParamSingle(4));
                    ConstantDefinitions.Add(c);
                    break;
                case Opcode.DefI:
                    var ci = new ConstantInt(instruction.GetParamRegisterNumber(0),
                        instruction.Params[1],
                        instruction.Params[2],
                        instruction.Params[3],
                        instruction.Params[4]);
                    ConstantIntDefinitions.Add(ci);
                    break;
                case Opcode.DP2Add:
                    WriteLine("{0} = dot({1}, {2}) + {3};", GetDestinationName(instruction),
                        GetSourceName(instruction, 1), GetSourceName(instruction, 2), GetSourceName(instruction, 3));
                    break;
                case Opcode.Dp3:
                    WriteLine("{0} = dot({1}, {2});", GetDestinationName(instruction),
                        GetSourceName(instruction, 1), GetSourceName(instruction, 2));
                    break;
                case Opcode.Dp4:
                    WriteLine("{0} = dot({1}, {2});", GetDestinationName(instruction),
                        GetSourceName(instruction, 1), GetSourceName(instruction, 2));
                    break;
                case Opcode.Else:
                    indent = indent.Substring(0, indent.Length - 1);
                    WriteLine("} else {");
                    indent += "\t";
                    break;
                case Opcode.Endif:
                    indent = indent.Substring(0, indent.Length - 1);
                    WriteLine("}");
                    break;
                case Opcode.Exp:
                    WriteLine("{0} = exp2({1});", GetDestinationName(instruction), GetSourceName(instruction, 1));
                    break;
                case Opcode.Frc:
                    WriteLine("{0} = frac({1});", GetDestinationName(instruction), GetSourceName(instruction, 1));
                    break;
                case Opcode.If:
                    WriteLine("if ({0}) {{", GetSourceName(instruction, 0));
                    indent += "\t";
                    break;
                case Opcode.IfC:
                    if ((IfComparison)instruction.Modifier == IfComparison.GE &&
                        instruction.GetSourceModifier(0) == SourceModifier.AbsAndNegate &&
                        instruction.GetSourceModifier(1) == SourceModifier.Abs &&
                        instruction.GetParamRegisterName(0) + instruction.GetSourceSwizzleName(0) ==
                        instruction.GetParamRegisterName(1) + instruction.GetSourceSwizzleName(1))
                    {
                        WriteLine("if ({0} == 0) {{", instruction.GetParamRegisterName(0) + instruction.GetSourceSwizzleName(0));
                    }
                    else if ((IfComparison)instruction.Modifier == IfComparison.LT &&
                        instruction.GetSourceModifier(0) == SourceModifier.AbsAndNegate &&
                        instruction.GetSourceModifier(1) == SourceModifier.Abs &&
                        instruction.GetParamRegisterName(0) + instruction.GetSourceSwizzleName(0) ==
                        instruction.GetParamRegisterName(1) + instruction.GetSourceSwizzleName(1))
                    {
                        WriteLine("if ({0} != 0) {{", instruction.GetParamRegisterName(0) + instruction.GetSourceSwizzleName(0));
                    }
                    else
                    {
                        string ifComparison;
                        switch ((IfComparison)instruction.Modifier)
                        {
                            case IfComparison.GT:
                                ifComparison = ">";
                                break;
                            case IfComparison.EQ:
                                ifComparison = "==";
                                break;
                            case IfComparison.GE:
                                ifComparison = ">=";
                                break;
                            case IfComparison.LE:
                                ifComparison = "<=";
                                break;
                            case IfComparison.NE:
                                ifComparison = "!=";
                                break;
                            case IfComparison.LT:
                                ifComparison = "<";
                                break;
                            default:
                                throw new InvalidOperationException();
                        }
                        WriteLine("if ({0} {2} {1}) {{", GetSourceName(instruction, 0), GetSourceName(instruction, 1), ifComparison);
                    }
                    indent += "\t";
                    break;
                case Opcode.Log:
                    WriteLine("{0} = log2({1});", GetDestinationName(instruction), GetSourceName(instruction, 1));
                    break;
                case Opcode.Lrp:
                    WriteLine("{0} = lerp({2}, {3}, {1});", GetDestinationName(instruction),
                        GetSourceName(instruction, 1), GetSourceName(instruction, 2), GetSourceName(instruction, 3));
                    break;
                case Opcode.Mad:
                    WriteLine("{0} = {1} * {2} + {3};", GetDestinationName(instruction),
                        GetSourceName(instruction, 1), GetSourceName(instruction, 2), GetSourceName(instruction, 3));
                    break;
                case Opcode.Max:
                    WriteLine("{0} = max({1}, {2});", GetDestinationName(instruction),
                        GetSourceName(instruction, 1), GetSourceName(instruction, 2));
                    break;
                case Opcode.Min:
                    WriteLine("{0} = min({1}, {2});", GetDestinationName(instruction),
                        GetSourceName(instruction, 1), GetSourceName(instruction, 2));
                    break;
                case Opcode.Mov:
                    WriteLine("{0} = {1};", GetDestinationName(instruction), GetSourceName(instruction, 1));
                    break;
                case Opcode.MovA:
                    WriteLine("{0} = {1};", GetDestinationName(instruction), GetSourceName(instruction, 1));
                    break;
                case Opcode.Mul:
                    WriteLine("{0} = {1} * {2};", GetDestinationName(instruction),
                        GetSourceName(instruction, 1), GetSourceName(instruction, 2));
                    break;
                case Opcode.Nrm:
                    WriteLine("{0} = normalize({1});", GetDestinationName(instruction), GetSourceName(instruction, 1));
                    break;
                case Opcode.Pow:
                    WriteLine("{0} = pow({1}, {2});", GetDestinationName(instruction),
                        GetSourceName(instruction, 1), GetSourceName(instruction, 2));
                    break;
                case Opcode.Rcp:
                    WriteLine("{0} = 1 / {1};", GetDestinationName(instruction), GetSourceName(instruction, 1));
                    break;
                case Opcode.Rsq:
                    WriteLine("{0} = 1 / sqrt({1});", GetDestinationName(instruction), GetSourceName(instruction, 1));
                    break;
                case Opcode.Sge:
                    if (instruction.GetSourceModifier(1) == SourceModifier.AbsAndNegate &&
                        instruction.GetSourceModifier(2) == SourceModifier.Abs &&
                        instruction.GetParamRegisterName(1) + instruction.GetSourceSwizzleName(1) ==
                        instruction.GetParamRegisterName(2) + instruction.GetSourceSwizzleName(2))
                    {
                        WriteLine("{0} = ({1} == 0) ? 1 : 0;", GetDestinationName(instruction),
                            instruction.GetParamRegisterName(1) + instruction.GetSourceSwizzleName(1));
                    }
                    else
                    {
                        WriteLine("{0} = ({1} >= {2}) ? 1 : 0;", GetDestinationName(instruction), GetSourceName(instruction, 1),
                            GetSourceName(instruction, 2));
                    }
                    break;
                case Opcode.Slt:
                    WriteLine("{0} = ({1} < {2}) ? 1 : 0;", GetDestinationName(instruction), GetSourceName(instruction, 1),
                        GetSourceName(instruction, 2));
                    break;
                case Opcode.SinCos:
                    WriteLine("sincos({1}, {0}, {0});", GetDestinationName(instruction), GetSourceName(instruction, 1));
                    break;
                case Opcode.Sub:
                    WriteLine("{0} = {1} - {2};", GetDestinationName(instruction),
                        GetSourceName(instruction, 1), GetSourceName(instruction, 2));
                    break;
                case Opcode.Tex:
                    if ((shader.MajorVersion == 1 && shader.MinorVersion >= 4) || (shader.MajorVersion > 1))
                    {
                        WriteLine("{0} = tex2D({2}, {1});", GetDestinationName(instruction),
                            GetSourceName(instruction, 1), GetSourceName(instruction, 2));
                    }
                    else
                    {
                        WriteLine("{0} = tex2D();", GetDestinationName(instruction));
                    }
                    break;
                case Opcode.TexLDL:
                    WriteLine("{0} = tex2Dlod({2}, {1});", GetDestinationName(instruction),
                        GetSourceName(instruction, 1), GetSourceName(instruction, 2));
                    break;
                case Opcode.Comment:
                case Opcode.End:
                    break;
            }
        }

        public void Write(string hlslFilename)
        {
            hlslFile = new FileStream(hlslFilename, FileMode.Create, FileAccess.Write);
            hlslWriter = new StreamWriter(hlslFile);

            ConstantDefinitions = new List<Constant>();
            ConstantIntDefinitions = new List<ConstantInt>();
            ConstantDeclarations = new List<ConstantDeclaration>();

            // Look for dcl instructions
            RegisterDeclarations = new List<RegisterDeclaration>();
            foreach (var declInstruction in shader.Instructions.Where(x => x.Opcode == Opcode.Dcl))
            {
                var decl = new RegisterDeclaration(declInstruction);
                RegisterDeclarations.Add(decl);
            }

            // Look for and parse the constant table
            int ctabToken = MakeFourCC("CTAB");
            var ctabComment = shader.Instructions.FirstOrDefault(x => x.Opcode == Opcode.Comment && x.Params[0] == ctabToken);
            if (ctabComment != null)
            {
                ParseConstantTable(ctabComment);
            }

            string methodTypeName;
            string methodParamList = "";
            string methodSemantic = "";

            // Create the input structure
            var dclInputs = RegisterDeclarations.Where(x => x.RegisterType == RegisterType.Input || x.RegisterType == RegisterType.MiscType);
            numInputs = dclInputs.Count();
            if (numInputs == 0)
            {
                System.Diagnostics.Debug.Assert(shader.Type == ShaderType.Pixel);
            }
            else if (numInputs == 1)
            {
                var dclInput = dclInputs.Single();
                methodParamList = string.Format("{0} {1} : {2}",
                    dclInput.TypeName, dclInput.Name, dclInput.Semantic);
            }
            else
            {
                if (shader.Type == ShaderType.Pixel)
                {
                    methodParamList = "VS_OUT i";
                    WriteLine("struct VS_OUT");
                }
                else
                {
                    methodParamList = "VS_IN i";
                    WriteLine("struct VS_IN");
                }
                WriteLine("{");
                indent = "\t";
                foreach (var dclInput in dclInputs)
                {
                    WriteLine("{0} {1} : {2};",
                        dclInput.TypeName, dclInput.Name, dclInput.Semantic);
                }
                indent = "";
                WriteLine("};");
                WriteLine();
            }

            // Create the output structure
            if (shader.Type == ShaderType.Vertex)
            {
                var dclOutputs = RegisterDeclarations.Where(x => x.RegisterType == RegisterType.Output || x.RegisterType == RegisterType.ColorOut);
                numOutputs = dclOutputs.Count();
                if (numOutputs == 1)
                {
                    var dclOutput = dclOutputs.Single();
                    methodTypeName = dclOutput.TypeName;
                    methodSemantic = " : " + dclOutput.Semantic;
                }
                else
                {
                    methodTypeName = "VS_OUT";
                    WriteLine("struct VS_OUT");
                    WriteLine("{");
                    indent = "\t";
                    foreach (var dclOutput in dclOutputs)
                    {
                        WriteLine("{0} {1} : {2};",
                            dclOutput.TypeName, dclOutput.Name, dclOutput.Semantic);
                    }
                    indent = "";
                    WriteLine("};");
                    WriteLine();
                }
            }
            else
            {
                // Find all assignments to pixel shader color outputs.
                Dictionary<string, int> colorRegisters = new Dictionary<string, int>();
                foreach (Instruction instruction in shader.Instructions)
                {
                    if (!instruction.HasDestination)
                    {
                        continue;
                    }

                    int destIndex = instruction.GetDestinationParamIndex();
                    if (instruction.GetParamRegisterType(destIndex) == RegisterType.ColorOut)
                    {
                        string registerName = "oC" + instruction.GetParamRegisterNumber(destIndex).ToString();
                        if (!colorRegisters.ContainsKey(registerName))
                        {
                            colorRegisters.Add(registerName, 0);
                        }
                    }
                }

                if (colorRegisters.Count > 1)
                {
                    methodTypeName = "PS_OUT";
                    WriteLine("struct PS_OUT");
                }
                else
                {
                    methodTypeName = "float4";
                    methodSemantic = " : COLOR";
                }
            }


            WriteLine("{0} main({1}){2}", methodTypeName, methodParamList, methodSemantic);
            WriteLine("{");
            indent = "\t";
            WriteLine("{0} o;", methodTypeName);
            WriteLine();

            // Find all assignments to temporary variables
            // and declare the variables.
            Dictionary<string, int> tempRegisters = new Dictionary<string, int>();
            foreach (Instruction instruction in shader.Instructions)
            {
                if (!instruction.HasDestination)
                {
                    continue;
                }

                int destIndex = instruction.GetDestinationParamIndex();
                if (instruction.GetParamRegisterType(destIndex) == RegisterType.Temp)
                {
                    string registerName = instruction.GetParamRegisterName(destIndex);
                    if (!tempRegisters.ContainsKey(registerName))
                    {
                        tempRegisters.Add(registerName, 0);
                    }
                    tempRegisters[registerName] |= instruction.GetDestinationWriteMask();
                }
            }

            foreach (var registerName in tempRegisters.Keys)
            {
                int writeMask = tempRegisters[registerName];
                string writeMaskName;
                switch (writeMask)
                {
                    case 0x1:
                        writeMaskName = "float";
                        break;
                    case 0x3:
                        writeMaskName = "float2";
                        break;
                    case 0x7:
                        writeMaskName = "float3";
                        break;
                    case 0xF:
                        writeMaskName = "float4";
                        break;
                    default:
                        // TODO
                        writeMaskName = "float4";
                        break;
                        //throw new NotImplementedException();
                }
                WriteLine("{0} {1};", writeMaskName, registerName);
            }

            foreach (Instruction instruction in shader.Instructions)
            {
                WriteInstruction(instruction);
            }

            WriteLine();
            WriteLine("return o;");
            indent = "";
            WriteLine("}");

            hlslWriter.Dispose();
            hlslFile.Dispose();
        }
    }
}