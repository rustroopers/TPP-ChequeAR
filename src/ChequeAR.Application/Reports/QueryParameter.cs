using System;

namespace ChequeAR.Application.Reports
{
    /// <summary>
    /// Tipo de un parámetro de consulta, en términos del NEGOCIO y no del motor.
    ///
    /// Existe porque tres casos necesitan que el driver sepa exactamente qué está recibiendo
    /// y no puede deducirlo del tipo CLR: los enteros de paginación, el identificador único
    /// (que se guarda como binario de 16 bytes) y las fechas. El resto se infiere del valor.
    /// </summary>
    public enum QueryParameterType
    {
        /// <summary>El driver infiere del valor. Cubre texto y números sueltos.</summary>
        Auto,
        Int32,
        /// <summary>Identificador único como binario de 16 bytes.</summary>
        Raw16,
        Date,
        Decimal
    }

    /// <summary>
    /// Parámetro de una consulta, sin atarse a ningún motor.
    ///
    /// Antes acá viajaba un OracleParameter, y eso obligaba a que TODA la capa de negocio
    /// conociera el driver — contradiciendo de frente lo que el documento de arquitectura
    /// afirma sobre aislamiento de la persistencia. La traducción al parámetro concreto vive
    /// ahora en la capa de infraestructura, que es donde corresponde.
    ///
    /// Nota honesta sobre el alcance: el TEXTO de la consulta sigue siendo dialecto del motor
    /// actual (marcadores :nombre, paginación OFFSET/FETCH). Lo que se elimina es la
    /// dependencia sobre los COMPONENTES del motor, no sobre su dialecto.
    /// </summary>
    public class QueryParameter
    {
        public string Name { get; }
        public object Value { get; }
        public QueryParameterType Type { get; }

        public QueryParameter(string name, object value, QueryParameterType type = QueryParameterType.Auto)
        {
            Name = name;
            Value = value;
            Type = type;
        }

        /// <summary>
        /// Identificador único. Se transporta como los 16 bytes crudos porque es así como
        /// está guardado; convertirlo a texto acá haría que la comparación nunca matchee.
        /// </summary>
        public static QueryParameter Id(string name, Guid value)
            => new QueryParameter(name, value.ToByteArray(), QueryParameterType.Raw16);

        public static QueryParameter Int(string name, int value)
            => new QueryParameter(name, value, QueryParameterType.Int32);

        public static QueryParameter Date(string name, DateTime value)
            => new QueryParameter(name, value, QueryParameterType.Date);

        public static QueryParameter Text(string name, object value)
            => new QueryParameter(name, value, QueryParameterType.Auto);

        public override string ToString() => Name + "=" + (Value ?? "<null>");
    }
}
