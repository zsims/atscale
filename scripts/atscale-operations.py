from troposphere import FindInMap, GetAtt, Parameter, Ref, Template, Join, Output
import troposphere.ec2 as ec2

template = Template()

# Parameters
key_name_param = template.add_parameter(Parameter(
    "KeyName",
    Description="Name of an existing EC2 KeyPair to enable RDP access to the instance",
    Type="String"
))

template.add_mapping('RegionMap', {
    "us-west-2": {"AMI": "ami-59f2d769"},
})

octopus_master_security_group = template.add_resource(ec2.SecurityGroup(
    "OctopusMasterSg",
    GroupDescription="Security group for the Octopus Master",
    SecurityGroupIngress=[
        # For example only. Shouldn't put this on the internet un-protected.
        ec2.SecurityGroupRule(
            IpProtocol="tcp",
            CidrIp="0.0.0.0/0",
            FromPort="80",
            ToPort="80"
        ),
        ec2.SecurityGroupRule(
            IpProtocol="tcp",
            CidrIp="0.0.0.0/0",
            FromPort="3389",
            ToPort="3389"
        )
    ]
))

# Note that Octopus Deploy Masters are not zero-config, and requires some initial setup we can't do here
octopus_deploy_server_instance1 = template.add_resource(ec2.Instance(
    "OctopusDeployServer1",
    ImageId=FindInMap("RegionMap", Ref("AWS::Region"), "AMI"),
    InstanceType="t1.micro",
    KeyName=Ref(key_name_param),
    SecurityGroups=[Ref(octopus_master_security_group)],
))

template.add_output(Output(
    "URL",
    Description="URL of the Octopus Deploy Server",
    Value=Join("", ["http://", GetAtt(octopus_deploy_server_instance1, "PublicDnsName")])
))

if __name__ == '__main__':
    with open('operations.json', 'w') as fh:
        contents = template.to_json()
        fh.write(contents)
